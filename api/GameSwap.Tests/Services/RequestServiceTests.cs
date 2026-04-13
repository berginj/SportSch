using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Azure;
using Azure.Data.Tables;
using GameSwap.Functions.Repositories;
using GameSwap.Functions.Services;
using GameSwap.Functions.Storage;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GameSwap.Tests.Services;

public class RequestServiceTests
{
    private readonly Mock<IRequestRepository> _mockRequestRepo;
    private readonly Mock<ISlotRepository> _mockSlotRepo;
    private readonly Mock<IMembershipRepository> _mockMembershipRepo;
    private readonly Mock<INotificationService> _mockNotificationService;
    private readonly Mock<INotificationPreferencesService> _mockPreferencesService;
    private readonly Mock<IEmailService> _mockEmailService;
    private readonly Mock<ILogger<RequestService>> _mockLogger;
    private readonly RequestService _service;

    public RequestServiceTests()
    {
        _mockRequestRepo = new Mock<IRequestRepository>();
        _mockSlotRepo = new Mock<ISlotRepository>();
        _mockMembershipRepo = new Mock<IMembershipRepository>();
        _mockNotificationService = new Mock<INotificationService>();
        _mockPreferencesService = new Mock<INotificationPreferencesService>();
        _mockEmailService = new Mock<IEmailService>();
        _mockLogger = new Mock<ILogger<RequestService>>();

        _service = new RequestService(
            _mockRequestRepo.Object,
            _mockSlotRepo.Object,
            _mockMembershipRepo.Object,
            _mockNotificationService.Object,
            _mockPreferencesService.Object,
            _mockEmailService.Object,
            _mockLogger.Object);
    }

    [Fact]
    public async Task CreateRequestAsync_ImmediateConfirm_NotifiesRequestingAndOfferingCoaches()
    {
        // Arrange
        var leagueId = "league-1";
        var division = "AAA";
        var slotId = "slot-123";
        var context = new CorrelationContext
        {
            UserId = "requesting-coach",
            UserEmail = "requesting@example.com",
            LeagueId = leagueId,
            CorrelationId = Guid.NewGuid().ToString()
        };
        var request = new CreateRequestRequest
        {
            LeagueId = leagueId,
            Division = division,
            SlotId = slotId,
            Notes = "Looks good"
        };
        TableEntity? updatedSlot = null;
        var notifications = new List<(string userId, string type, string message)>();
        var requestApprovedEmails = new List<string>();
        var requestReceivedEmails = new List<string>();

        var membership = new TableEntity(context.UserId, leagueId)
        {
            { "Role", Constants.Roles.Coach },
            { "Division", division },
            { "TeamId", "TEAM-B" },
            { "Email", context.UserEmail }
        };

        var slot = new TableEntity($"SLOT|{leagueId}|{division}", slotId)
        {
            { "Status", Constants.Status.SlotOpen },
            { "Division", division },
            { "OfferingTeamId", "TEAM-A" },
            { "AwayTeamId", "" },
            { "IsExternalOffer", false },
            { "IsAvailability", false },
            { "GameDate", "2026-06-15" },
            { "StartTime", "18:00" },
            { "EndTime", "19:30" },
            { "DisplayName", "Diamond 1" }
        };
        slot.ETag = new ETag("etag-1");

        _mockMembershipRepo
            .Setup(x => x.GetMembershipAsync(context.UserId, leagueId))
            .ReturnsAsync(membership);
        _mockMembershipRepo
            .Setup(x => x.IsGlobalAdminAsync(context.UserId))
            .ReturnsAsync(false);
        _mockMembershipRepo
            .Setup(x => x.GetLeagueMembershipsAsync(leagueId))
            .ReturnsAsync(new List<TableEntity>
            {
                new("offering-coach", leagueId)
                {
                    { "Role", Constants.Roles.Coach },
                    { "Division", division },
                    { "TeamId", "TEAM-A" },
                    { "Email", "offering@example.com" }
                },
                new("requesting-coach", leagueId)
                {
                    { "Role", Constants.Roles.Coach },
                    { "Division", division },
                    { "TeamId", "TEAM-B" },
                    { "Email", "requesting@example.com" }
                }
            });

        _mockSlotRepo
            .Setup(x => x.GetSlotAsync(leagueId, division, slotId))
            .ReturnsAsync(slot);
        _mockSlotRepo
            .Setup(x => x.UpdateSlotAsync(It.IsAny<TableEntity>(), It.IsAny<ETag>()))
            .Callback<TableEntity, ETag>((entity, _) => updatedSlot = entity)
            .Returns(Task.CompletedTask);
        _mockSlotRepo
            .Setup(x => x.QuerySlotsAsync(It.IsAny<SlotQueryFilter>(), null))
            .ReturnsAsync(new PaginationResult<TableEntity>
            {
                Items = new List<TableEntity>(),
                PageSize = 100,
                ContinuationToken = null
            });

        _mockRequestRepo
            .Setup(x => x.CreateRequestAsync(It.IsAny<TableEntity>()))
            .Returns(Task.CompletedTask);
        _mockRequestRepo
            .Setup(x => x.GetPendingRequestsForSlotAsync(leagueId, division, slotId))
            .ReturnsAsync(new List<TableEntity>());

        _mockNotificationService
            .Setup(x => x.CreateNotificationAsync(
                It.IsAny<string>(),
                leagueId,
                It.IsAny<string>(),
                It.IsAny<string>(),
                "#calendar",
                slotId,
                "Slot"))
            .Callback<string, string, string, string, string?, string?, string?>((recipientUserId, _, type, message, _, _, _) =>
            {
                notifications.Add((recipientUserId, type, message));
            })
            .ReturnsAsync(Guid.NewGuid().ToString());

        _mockPreferencesService
            .Setup(x => x.ShouldSendEmailAsync("requesting-coach", leagueId, "RequestApproved"))
            .ReturnsAsync(true);
        _mockPreferencesService
            .Setup(x => x.ShouldSendEmailAsync("offering-coach", leagueId, "RequestReceived"))
            .ReturnsAsync(true);

        _mockEmailService
            .Setup(x => x.SendRequestApprovedEmailAsync("requesting@example.com", leagueId, "2026-06-15", "18:00", "Diamond 1"))
            .Callback(() => requestApprovedEmails.Add("requesting@example.com"))
            .Returns(Task.CompletedTask);
        _mockEmailService
            .Setup(x => x.SendRequestReceivedEmailAsync("offering@example.com", leagueId, "TEAM-B", "2026-06-15", "18:00"))
            .Callback(() => requestReceivedEmails.Add("offering@example.com"))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.CreateRequestAsync(request, context);
        await WaitForConditionAsync(() =>
            notifications.Count == 2 &&
            requestApprovedEmails.Count == 1 &&
            requestReceivedEmails.Count == 1);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(updatedSlot);
        Assert.Equal("TEAM-B", updatedSlot!.GetString("ConfirmedTeamId"));
        Assert.Equal("", updatedSlot.GetString("AwayTeamId"));

        Assert.Contains(notifications, item =>
            item.userId == "requesting-coach" &&
            item.type == "RequestApproved" &&
            item.message.Contains("Game confirmed", StringComparison.Ordinal));

        Assert.Contains(notifications, item =>
            item.userId == "offering-coach" &&
            item.type == "RequestReceived" &&
            item.message.Contains("TEAM-B accepted your open game", StringComparison.Ordinal));

        Assert.Single(requestApprovedEmails);
        Assert.Single(requestReceivedEmails);
    }

    private static async Task WaitForConditionAsync(Func<bool> condition, int timeoutMs = 1000)
    {
        var started = DateTime.UtcNow;
        while (!condition())
        {
            if ((DateTime.UtcNow - started).TotalMilliseconds >= timeoutMs)
            {
                throw new TimeoutException("Condition was not met within the allotted time.");
            }

            await Task.Delay(25);
        }
    }
}
