using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Azure;
using Azure.Data.Tables;
using GameSwap.Functions.Models;
using GameSwap.Functions.Repositories;
using GameSwap.Functions.Services;
using GameSwap.Functions.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GameSwap.Tests.Services;

public class GameRescheduleSafetyTests
{
    private readonly Mock<IGameRescheduleRequestRepository> _requests = new();
    private readonly Mock<ISlotRepository> _slots = new();
    private readonly Mock<IMembershipRepository> _members = new();
    private readonly TableEntity _request = new("GAMERESCHEDULE|league", "move")
    {
        ["Division"] = "10U", ["OriginalSlotId"] = "original", ["ProposedSlotId"] = "replacement",
        ["OpponentTeamId"] = "away", ["RequestingTeamId"] = "home",
        ["Status"] = GameRescheduleRequestStatuses.ApprovedByBothTeams
    };
    private readonly TableEntity _original = new("SLOT|league|10U", "original")
    {
        ["Status"] = Constants.Status.SlotConfirmed, ["GameDate"] = "2026-07-20", ["StartTime"] = "18:00",
        ["HomeTeamId"] = "home", ["ConfirmedTeamId"] = "away", ["ConfirmedRequestId"] = "acceptance"
    };
    private readonly TableEntity _replacement = new("SLOT|league|10U", "replacement")
    { ["Status"] = Constants.Status.SlotOpen, ["IsAvailability"] = true, ["GameDate"] = "2026-07-21", ["StartTime"] = "18:00", ["EndTime"] = "19:30" };

    private GameRescheduleRequestService Service()
    {
        _requests.Setup(x => x.GetRequestAsync("league", "move")).ReturnsAsync(_request);
        _slots.Setup(x => x.GetSlotAsync("league", "10U", "original")).ReturnsAsync(_original);
        _slots.Setup(x => x.GetSlotAsync("league", "10U", "replacement")).ReturnsAsync(_replacement);
        _slots.Setup(x => x.QuerySlotsAsync(It.IsAny<SlotQueryFilter>(), null))
            .ReturnsAsync(new PaginationResult<TableEntity> { Items = new(), PageSize = 100 });
        _members.Setup(x => x.IsGlobalAdminAsync("admin")).ReturnsAsync(true);
        _members.Setup(x => x.GetLeagueMembershipsAsync("league")).ReturnsAsync(new List<TableEntity>());
        return new(_requests.Object, _slots.Object, _members.Object, Mock.Of<INotificationService>(),
            NullLogger<GameRescheduleRequestService>.Instance, new FixedClock());
    }

    [Fact]
    public async Task UnavailableReplacementDoesNotCancelOriginal()
    {
        _replacement["Status"] = Constants.Status.SlotConfirmed;
        var error = await Assert.ThrowsAsync<ApiGuards.HttpError>(() => Service().FinalizeAsync("league", "admin", "move"));
        Assert.Equal(409, error.Status);
        Assert.Equal(Constants.Status.SlotConfirmed, _original.GetString("Status"));
        _slots.Verify(x => x.UpdateSlotsAtomicallyAsync(It.IsAny<IReadOnlyList<TableEntity>>()), Times.Never);
    }

    [Fact]
    public async Task MoveUsesOneConditionalTransactionAndKeepsAcceptanceHistoryAtOriginalSlot()
    {
        await Service().FinalizeAsync("league", "admin", "move");
        _slots.Verify(x => x.UpdateSlotsAtomicallyAsync(It.Is<IReadOnlyList<TableEntity>>(rows => rows.Count == 2)), Times.Once);
        _slots.Verify(x => x.UpdateSlotAsync(It.IsAny<TableEntity>(), It.IsAny<ETag>()), Times.Never);
        Assert.Equal("acceptance", _replacement.GetString("MovedFromConfirmedRequestId"));
        Assert.Equal("original", _replacement.GetString("MovedFromSlotId"));
        Assert.Equal("", _replacement.GetString("ConfirmedRequestId"));
        Assert.Equal("away", _replacement.GetString("ConfirmedTeamId"));
    }

    [Fact]
    public async Task ReplayAfterSlotCommitDoesNotMoveSlotsAgain()
    {
        _original["RescheduleOperationId"] = "move";
        _original["Status"] = Constants.Status.SlotCancelled;
        _replacement["RescheduleOperationId"] = "move";
        _replacement["Status"] = Constants.Status.SlotConfirmed;
        await Service().FinalizeAsync("league", "admin", "move");
        Assert.Equal(GameRescheduleRequestStatuses.Finalized, _request.GetString("Status"));
        _slots.Verify(x => x.UpdateSlotsAtomicallyAsync(It.IsAny<IReadOnlyList<TableEntity>>()), Times.Never);
    }

    [Fact]
    public async Task StaleTransactionReturnsConflict()
    {
        _slots.Setup(x => x.UpdateSlotsAtomicallyAsync(It.IsAny<IReadOnlyList<TableEntity>>()))
            .ThrowsAsync(new RequestFailedException(412, "Changed"));
        var error = await Assert.ThrowsAsync<ApiGuards.HttpError>(() => Service().FinalizeAsync("league", "admin", "move"));
        Assert.Equal(409, error.Status);
        _requests.Verify(x => x.UpdateRequestAsync(It.IsAny<TableEntity>(), It.IsAny<ETag>()), Times.Never);
    }

    [Fact]
    public async Task SameTeamIdInAnotherDivisionCannotFinalize()
    {
        var service = Service();
        _members.Setup(x => x.GetMembershipAsync("coach", "league")).ReturnsAsync(new TableEntity("coach", "league")
            { ["Role"] = "Coach", ["Division"] = "12U", ["TeamId"] = "away" });
        var error = await Assert.ThrowsAsync<ApiGuards.HttpError>(() => service.FinalizeAsync("league", "coach", "move"));
        Assert.Equal(403, error.Status);
        _slots.Verify(x => x.UpdateSlotsAtomicallyAsync(It.IsAny<IReadOnlyList<TableEntity>>()), Times.Never);
    }
}
