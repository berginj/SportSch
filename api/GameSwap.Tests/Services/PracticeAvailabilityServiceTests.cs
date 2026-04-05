using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Azure.Data.Tables;
using GameSwap.Functions.Models;
using GameSwap.Functions.Repositories;
using GameSwap.Functions.Services;
using GameSwap.Functions.Storage;
using Moq;
using Xunit;

namespace GameSwap.Tests.Services;

public class PracticeAvailabilityServiceTests
{
    private readonly Mock<IMembershipRepository> _membershipRepository = new();
    private readonly Mock<ITeamRepository> _teamRepository = new();
    private readonly Mock<ISlotRepository> _slotRepository = new();
    private readonly Mock<IPracticeRequestRepository> _practiceRequestRepository = new();

    [Fact]
    public async Task CheckCoachAvailabilityAsync_OpenPracticeSlot_ReturnsAvailable()
    {
        var service = CreateService();
        var membership = new TableEntity("coach-1", "league-1")
        {
            ["Role"] = Constants.Roles.Coach,
            ["Division"] = "12U",
            ["TeamId"] = "TEAM-1",
            ["TeamName"] = "Storm",
        };
        var slot = new TableEntity("SLOT|league-1|12U", "slot-1")
        {
            ["SlotId"] = "slot-1",
            ["Division"] = "12U",
            ["GameDate"] = "2026-04-12",
            ["StartTime"] = "18:00",
            ["EndTime"] = "19:30",
            ["FieldKey"] = "agsa/barcroft-3",
            ["DisplayName"] = "AGSA > Barcroft #3",
            ["Status"] = Constants.Status.SlotOpen,
            ["IsAvailability"] = true,
            ["AllocationSlotType"] = "practice",
            ["PracticeSeasonLabel"] = "Spring 2026",
            ["PracticeBookingPolicy"] = FieldInventoryPracticeBookingPolicies.CommissionerReview,
        };

        _membershipRepository.Setup(x => x.IsGlobalAdminAsync("coach-1")).ReturnsAsync(false);
        _membershipRepository.Setup(x => x.GetMembershipAsync("coach-1", "league-1")).ReturnsAsync(membership);
        _slotRepository.Setup(x => x.QuerySlotsAsync(It.IsAny<SlotQueryFilter>(), null)).ReturnsAsync(new PaginationResult<TableEntity>
        {
            Items = [slot],
            PageSize = 500,
        });
        _practiceRequestRepository.Setup(x => x.QueryRequestsAsync("league-1", null, "12U", null, null))
            .ReturnsAsync(new List<TableEntity>());

        var result = await service.CheckCoachAvailabilityAsync(
            new PracticeAvailabilityQueryRequest("Spring 2026", "2026-04-12", "18:00", "19:30", null, null),
            "coach-1",
            CorrelationContext.Create("coach-1", "league-1"));

        Assert.True(result.Available);
        Assert.Single(result.Options);
        Assert.True(result.Options[0].IsAvailable);
        Assert.True(result.Options[0].Shareable);
        Assert.Equal(2, result.Options[0].MaxTeamsPerBooking);
    }

    [Fact]
    public async Task ListCoachAvailabilityOptionsAsync_SharedPendingRequest_ExposesReservedTeamsAndMarksUnavailable()
    {
        var service = CreateService();
        var membership = new TableEntity("coach-1", "league-1")
        {
            ["Role"] = Constants.Roles.Coach,
            ["Division"] = "12U",
            ["TeamId"] = "TEAM-1",
            ["TeamName"] = "Storm",
        };
        var slot = new TableEntity("SLOT|league-1|12U", "slot-1")
        {
            ["SlotId"] = "slot-1",
            ["Division"] = "12U",
            ["GameDate"] = "2026-04-12",
            ["StartTime"] = "18:00",
            ["EndTime"] = "19:30",
            ["FieldKey"] = "agsa/barcroft-3",
            ["DisplayName"] = "AGSA > Barcroft #3",
            ["Status"] = Constants.Status.SlotOpen,
            ["IsAvailability"] = true,
            ["AllocationSlotType"] = "practice",
            ["PracticeSeasonLabel"] = "Spring 2026",
            ["PracticeBookingPolicy"] = FieldInventoryPracticeBookingPolicies.AutoApprove,
        };
        var pendingRequest = new TableEntity("PRACTICEREQ|league-1", "req-1")
        {
            ["Division"] = "12U",
            ["SlotId"] = "slot-1",
            ["TeamId"] = "TEAM-2",
            ["Status"] = FieldInventoryPracticeRequestStatuses.Pending,
            ["OpenToShareField"] = true,
            ["ShareWithTeamId"] = "TEAM-3",
        };

        _membershipRepository.Setup(x => x.IsGlobalAdminAsync("coach-1")).ReturnsAsync(false);
        _membershipRepository.Setup(x => x.GetMembershipAsync("coach-1", "league-1")).ReturnsAsync(membership);
        _slotRepository.Setup(x => x.QuerySlotsAsync(It.IsAny<SlotQueryFilter>(), null)).ReturnsAsync(new PaginationResult<TableEntity>
        {
            Items = [slot],
            PageSize = 500,
        });
        _practiceRequestRepository.Setup(x => x.QueryRequestsAsync("league-1", null, "12U", null, null))
            .ReturnsAsync([pendingRequest]);

        var result = await service.GetCoachAvailabilityOptionsAsync(
            new PracticeAvailabilityQueryRequest("Spring 2026", "2026-04-12", null, null, null, null),
            "coach-1",
            CorrelationContext.Create("coach-1", "league-1"));

        var option = Assert.Single(result.Options);
        Assert.False(option.IsAvailable);
        Assert.Single(option.PendingTeamIds);
        Assert.Single(option.PendingShareTeamIds);
        Assert.Equal("TEAM-2", option.PendingTeamIds[0]);
        Assert.Equal("TEAM-3", option.PendingShareTeamIds[0]);
    }

    private PracticeAvailabilityService CreateService()
        => new(
            _membershipRepository.Object,
            _teamRepository.Object,
            _slotRepository.Object,
            _practiceRequestRepository.Object);
}
