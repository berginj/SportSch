using System;
using System.Collections.Generic;
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

/// <summary>
/// Tests for request/slot confirmation atomicity and race condition handling.
/// These tests verify the critical fix for Issue #1 from SCHEDULING_LOGIC_REVIEW.md
/// </summary>
public class RequestServiceAtomicityTests
{
    private readonly Mock<IRequestRepository> _mockRequestRepo;
    private readonly Mock<ISlotRepository> _mockSlotRepo;
    private readonly Mock<IMembershipRepository> _mockMembershipRepo;
    private readonly Mock<INotificationService> _mockNotificationService;
    private readonly Mock<INotificationPreferencesService> _mockPreferencesService;
    private readonly Mock<IEmailService> _mockEmailService;
    private readonly Mock<ILogger<RequestService>> _mockLogger;
    private readonly RequestService _service;

    public RequestServiceAtomicityTests()
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
    public async Task CreateRequestAsync_ConcurrentAcceptance_SlotUpdateFailure_DoesNotCreateOrphanedRequest()
    {
        // CRITICAL TEST: Verifies atomicity fix (Issue #1)
        // Scenario: Two teams accept same slot simultaneously
        // Expected: Loser's slot update fails, NO request is created for loser

        // Arrange
        var leagueId = "league-1";
        var division = "10U";
        var slotId = "slot-race-test";
        var loserTeamId = "Lions";

        var loserContext = new CorrelationContext
        {
            UserId = "lions-coach",
            UserEmail = "lions@example.com",
            LeagueId = leagueId,
            CorrelationId = Guid.NewGuid().ToString()
        };

        var request = new CreateRequestRequest
        {
            LeagueId = leagueId,
            Division = division,
            SlotId = slotId,
            Notes = "We want this game"
        };

        var loserMembership = new TableEntity("lions-coach", leagueId)
        {
            { "Role", Constants.Roles.Coach },
            { "Division", division },
            { "TeamId", loserTeamId },
            { "Email", "lions@example.com" }
        };

        var slot = new TableEntity($"SLOT|{leagueId}|{division}", slotId)
        {
            { "Status", Constants.Status.SlotOpen },
            { "Division", division },
            { "OfferingTeamId", "Panthers" },
            { "HomeTeamId", "Panthers" },
            { "AwayTeamId", "" },
            { "IsExternalOffer", false },
            { "IsAvailability", false },
            { "GameDate", "2026-06-15" },
            { "StartTime", "18:00" },
            { "EndTime", "19:30" },
            { "FieldKey", "park1/field1" }
        };
        slot.ETag = new ETag("original-etag");

        _mockMembershipRepo
            .Setup(x => x.GetMembershipAsync("lions-coach", leagueId))
            .ReturnsAsync(loserMembership);
        _mockMembershipRepo
            .Setup(x => x.IsGlobalAdminAsync("lions-coach"))
            .ReturnsAsync(false);
        _mockMembershipRepo
            .Setup(x => x.GetLeagueMembershipsAsync(leagueId))
            .ReturnsAsync(new List<TableEntity>());

        _mockSlotRepo
            .Setup(x => x.GetSlotAsync(leagueId, division, slotId))
            .ReturnsAsync(slot);

        // Simulate: No team conflicts found (both teams are free at this time)
        _mockSlotRepo
            .Setup(x => x.QuerySlotsAsync(It.IsAny<SlotQueryFilter>(), null))
            .ReturnsAsync(new PaginationResult<TableEntity>
            {
                Items = new List<TableEntity>(),
                ContinuationToken = null,
                PageSize = 100
            });

        // CRITICAL: Simulate slot update failure (another team won the race)
        // This simulates ETag mismatch when Tigers confirmed the slot first
        _mockSlotRepo
            .Setup(x => x.UpdateSlotAsync(It.IsAny<TableEntity>(), It.IsAny<ETag>()))
            .ThrowsAsync(new RequestFailedException(412, "Precondition Failed", "ETag mismatch", null));

        _mockRequestRepo
            .Setup(x => x.GetPendingRequestsForSlotAsync(leagueId, division, slotId))
            .ReturnsAsync(new List<TableEntity>());

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ApiGuards.HttpError>(() =>
            _service.CreateRequestAsync(request, loserContext));

        // Verify error returned to user
        Assert.Equal(409, ex.Status);
        Assert.Equal(ErrorCodes.CONFLICT, ex.Code);
        Assert.Contains("confirmed by another team", ex.Message);

        // CRITICAL ASSERTION: Request was NEVER created (atomicity guarantee)
        _mockRequestRepo.Verify(
            x => x.CreateRequestAsync(It.IsAny<TableEntity>()),
            Times.Never,
            "Request should NOT be created when slot update fails (atomicity guarantee)");

        // Verify slot update was attempted (but failed)
        _mockSlotRepo.Verify(
            x => x.UpdateSlotAsync(It.IsAny<TableEntity>(), It.IsAny<ETag>()),
            Times.Once,
            "Slot update should be attempted first");
    }

    [Fact]
    public async Task CreateRequestAsync_SuccessfulAcceptance_CreatesRequestAfterSlotUpdate()
    {
        // CRITICAL TEST: Verifies correct operation ordering (slot first, request second)
        // Ensures request is only created after slot successfully confirmed

        // Arrange
        var leagueId = "league-1";
        var division = "10U";
        var slotId = "slot-success";
        var teamId = "Tigers";

        var context = new CorrelationContext
        {
            UserId = "coach-1",
            UserEmail = "coach@example.com",
            LeagueId = leagueId,
            CorrelationId = Guid.NewGuid().ToString()
        };

        var request = new CreateRequestRequest
        {
            LeagueId = leagueId,
            Division = division,
            SlotId = slotId,
            Notes = "Accept"
        };

        var membership = new TableEntity("coach-1", leagueId)
        {
            { "Role", Constants.Roles.Coach },
            { "Division", division },
            { "TeamId", teamId }
        };

        var slot = new TableEntity($"SLOT|{leagueId}|{division}", slotId)
        {
            { "Status", Constants.Status.SlotOpen },
            { "Division", division },
            { "OfferingTeamId", "Panthers" },
            { "GameDate", "2026-06-15" },
            { "StartTime", "18:00" },
            { "EndTime", "19:30" },
            { "FieldKey", "park1/field1" },
            { "IsAvailability", false },
            { "IsExternalOffer", false }
        };
        slot.ETag = new ETag("etag-1");

        var operationOrder = new List<string>();

        _mockMembershipRepo
            .Setup(x => x.GetMembershipAsync("coach-1", leagueId))
            .ReturnsAsync(membership);
        _mockMembershipRepo
            .Setup(x => x.IsGlobalAdminAsync("coach-1"))
            .ReturnsAsync(false);
        _mockMembershipRepo
            .Setup(x => x.GetLeagueMembershipsAsync(leagueId))
            .ReturnsAsync(new List<TableEntity>());

        _mockSlotRepo
            .Setup(x => x.GetSlotAsync(leagueId, division, slotId))
            .ReturnsAsync(slot);
        _mockSlotRepo
            .Setup(x => x.QuerySlotsAsync(It.IsAny<SlotQueryFilter>(), null))
            .ReturnsAsync(new PaginationResult<TableEntity>
            {
                Items = new List<TableEntity>(),
                PageSize = 100
            });

        // Track operation order
        _mockSlotRepo
            .Setup(x => x.UpdateSlotAsync(It.IsAny<TableEntity>(), It.IsAny<ETag>()))
            .Callback(() => operationOrder.Add("SlotUpdate"))
            .Returns(Task.CompletedTask);

        _mockRequestRepo
            .Setup(x => x.CreateRequestAsync(It.IsAny<TableEntity>()))
            .Callback(() => operationOrder.Add("RequestCreate"))
            .Returns(Task.CompletedTask);

        _mockRequestRepo
            .Setup(x => x.GetPendingRequestsForSlotAsync(leagueId, division, slotId))
            .ReturnsAsync(new List<TableEntity>());

        // Act
        var result = await _service.CreateRequestAsync(request, context);

        // Assert
        Assert.NotNull(result);

        // CRITICAL ASSERTION: Verify operation ordering
        Assert.Equal(2, operationOrder.Count);
        Assert.Equal("SlotUpdate", operationOrder[0]);
        Assert.Equal("RequestCreate", operationOrder[1]);

        // Verify slot was updated to Confirmed
        _mockSlotRepo.Verify(
            x => x.UpdateSlotAsync(
                It.Is<TableEntity>(s =>
                    s.GetString("Status") == Constants.Status.SlotConfirmed &&
                    s.GetString("ConfirmedTeamId") == teamId),
                It.IsAny<ETag>()),
            Times.Once);

        // Verify request was created with Approved status
        _mockRequestRepo.Verify(
            x => x.CreateRequestAsync(
                It.Is<TableEntity>(r =>
                    r.GetString("Status") == Constants.Status.SlotRequestApproved &&
                    r.GetString("RequestingTeamId") == teamId)),
            Times.Once);
    }

    [Fact]
    public async Task CreateRequestAsync_RapidOpenSlotAcceptance_PreventedByEnhancedDoubleBookingCheck()
    {
        // CRITICAL TEST: Verifies enhanced double-booking detection (Issue #2)
        // Scenario: Team tries to accept overlapping Open slot while already having another Open slot
        // Expected: Rejected with DOUBLE_BOOKING error

        // Arrange
        var leagueId = "league-1";
        var division = "10U";
        var teamId = "Tigers";

        var context = new CorrelationContext
        {
            UserId = "coach-1",
            UserEmail = "coach@example.com",
            LeagueId = leagueId
        };

        // Team trying to accept this slot at 3:30pm-5:30pm
        var newSlotRequest = new CreateRequestRequest
        {
            LeagueId = leagueId,
            Division = division,
            SlotId = "slot-2",
            Notes = "Second game"
        };

        var membership = new TableEntity("coach-1", leagueId)
        {
            { "Role", Constants.Roles.Coach },
            { "Division", division },
            { "TeamId", teamId }
        };

        // The slot being accepted (3:30pm-5:30pm)
        var newSlot = new TableEntity($"SLOT|{leagueId}|{division}", "slot-2")
        {
            { "Status", Constants.Status.SlotOpen },
            { "Division", division },
            { "OfferingTeamId", "Panthers" },
            { "GameDate", "2026-06-15" },
            { "StartTime", "15:30" },  // 3:30pm
            { "EndTime", "17:30" },    // 5:30pm
            { "StartMin", 930 },
            { "EndMin", 1050 },
            { "IsAvailability", false },
            { "IsExternalOffer", false }
        };
        newSlot.ETag = new ETag("etag-new");

        // Existing OPEN slot team already has (3:00pm-5:00pm) - OVERLAPS!
        var existingOpenSlot = new TableEntity($"SLOT|{leagueId}|{division}", "slot-1")
        {
            { "Status", Constants.Status.SlotOpen },  // OPEN, not Confirmed
            { "Division", division },
            { "HomeTeamId", teamId },  // Tigers involved
            { "OfferingTeamId", teamId },
            { "GameDate", "2026-06-15" },
            { "StartTime", "15:00" },  // 3:00pm
            { "EndTime", "17:00" },    // 5:00pm
            { "StartMin", 900 },
            { "EndMin", 1020 }
        };

        _mockMembershipRepo
            .Setup(x => x.GetMembershipAsync("coach-1", leagueId))
            .ReturnsAsync(membership);
        _mockMembershipRepo
            .Setup(x => x.IsGlobalAdminAsync("coach-1"))
            .ReturnsAsync(false);

        _mockSlotRepo
            .Setup(x => x.GetSlotAsync(leagueId, division, "slot-2"))
            .ReturnsAsync(newSlot);

        // Return the existing Open slot in conflict query
        // This verifies enhanced double-booking check includes Open slots
        _mockSlotRepo
            .Setup(x => x.QuerySlotsAsync(
                It.Is<SlotQueryFilter>(f =>
                    f.Statuses != null &&
                    f.Statuses.Contains(Constants.Status.SlotOpen) &&
                    f.Statuses.Contains(Constants.Status.SlotConfirmed)),
                null))
            .ReturnsAsync(new PaginationResult<TableEntity>
            {
                Items = new List<TableEntity> { existingOpenSlot },
                PageSize = 100
            });

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ApiGuards.HttpError>(() =>
            _service.CreateRequestAsync(newSlotRequest, context));

        Assert.Equal(409, ex.Status);
        Assert.Equal(ErrorCodes.DOUBLE_BOOKING, ex.Code);
        Assert.Contains("overlaps", ex.Message.ToLower());

        // Verify slot was NEVER updated (caught in validation)
        _mockSlotRepo.Verify(
            x => x.UpdateSlotAsync(It.IsAny<TableEntity>(), It.IsAny<ETag>()),
            Times.Never,
            "Slot should not be updated when double-booking detected");

        // Verify request was NEVER created
        _mockRequestRepo.Verify(
            x => x.CreateRequestAsync(It.IsAny<TableEntity>()),
            Times.Never,
            "Request should not be created when double-booking detected");
    }

    [Fact]
    public async Task CreateRequestAsync_AllFourTeamFields_CheckedForConflicts()
    {
        // CRITICAL TEST: Verifies all team fields checked (Issue #2 enhancement)
        // Tests that HomeTeamId, AwayTeamId, OfferingTeamId, ConfirmedTeamId all checked

        // Arrange
        var leagueId = "league-1";
        var division = "10U";
        var teamId = "Tigers";

        var context = new CorrelationContext
        {
            UserId = "coach-1",
            UserEmail = "coach@example.com",
            LeagueId = leagueId
        };

        var request = new CreateRequestRequest
        {
            LeagueId = leagueId,
            Division = division,
            SlotId = "new-slot",
            Notes = ""
        };

        var membership = new TableEntity("coach-1", leagueId)
        {
            { "Role", Constants.Roles.Coach },
            { "Division", division },
            { "TeamId", teamId }
        };

        var newSlot = new TableEntity($"SLOT|{leagueId}|{division}", "new-slot")
        {
            { "Status", Constants.Status.SlotOpen },
            { "Division", division },
            { "OfferingTeamId", "Panthers" },
            { "GameDate", "2026-06-15" },
            { "StartTime", "16:00" },
            { "EndTime", "17:30" },
            { "StartMin", 960 },
            { "EndMin", 1050 },
            { "IsAvailability", false },
            { "IsExternalOffer", false }
        };
        newSlot.ETag = new ETag("etag");

        // Existing slot where Tigers is in AwayTeamId field (not OfferingTeamId or ConfirmedTeamId)
        var conflictSlot = new TableEntity($"SLOT|{leagueId}|{division}", "conflict-slot")
        {
            { "Status", Constants.Status.SlotConfirmed },
            { "Division", division },
            { "HomeTeamId", "Lions" },
            { "AwayTeamId", teamId },  // Tigers in AwayTeamId
            { "OfferingTeamId", "Lions" },
            { "ConfirmedTeamId", teamId },
            { "GameDate", "2026-06-15" },
            { "StartTime", "15:00" },
            { "EndTime", "16:30" },  // Overlaps with 16:00-17:30
            { "StartMin", 900 },
            { "EndMin", 990 }
        };

        _mockMembershipRepo
            .Setup(x => x.GetMembershipAsync("coach-1", leagueId))
            .ReturnsAsync(membership);
        _mockMembershipRepo
            .Setup(x => x.IsGlobalAdminAsync("coach-1"))
            .ReturnsAsync(false);

        _mockSlotRepo
            .Setup(x => x.GetSlotAsync(leagueId, division, "new-slot"))
            .ReturnsAsync(newSlot);

        _mockSlotRepo
            .Setup(x => x.QuerySlotsAsync(It.IsAny<SlotQueryFilter>(), null))
            .ReturnsAsync(new PaginationResult<TableEntity>
            {
                Items = new List<TableEntity> { conflictSlot },
                PageSize = 100
            });

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ApiGuards.HttpError>(() =>
            _service.CreateRequestAsync(request, context));

        Assert.Equal(409, ex.Status);
        Assert.Equal(ErrorCodes.DOUBLE_BOOKING, ex.Code);

        // Verify the conflict was detected even though team was in AwayTeamId field
        Assert.Contains("overlaps", ex.Message.ToLower());
    }

    [Fact]
    public async Task CreateRequestAsync_ChecksBothConfirmedAndOpenSlots()
    {
        // CRITICAL TEST: Verifies enhanced query includes both Confirmed and Open slots
        // This is the key fix for Issue #2 (rapid Open slot acceptance)

        // Arrange
        var leagueId = "league-1";
        var division = "10U";

        var context = new CorrelationContext
        {
            UserId = "coach-1",
            LeagueId = leagueId
        };

        var request = new CreateRequestRequest
        {
            LeagueId = leagueId,
            Division = division,
            SlotId = "slot-1"
        };

        var membership = new TableEntity("coach-1", leagueId)
        {
            { "Role", Constants.Roles.Coach },
            { "Division", division },
            { "TeamId", "Tigers" }
        };

        var slot = new TableEntity($"SLOT|{leagueId}|{division}", "slot-1")
        {
            { "Status", Constants.Status.SlotOpen },
            { "GameDate", "2026-06-15" },
            { "StartTime", "18:00" },
            { "EndTime", "19:30" },
            { "OfferingTeamId", "Panthers" },
            { "IsAvailability", false },
            { "IsExternalOffer", false }
        };
        slot.ETag = new ETag("etag");

        _mockMembershipRepo
            .Setup(x => x.GetMembershipAsync("coach-1", leagueId))
            .ReturnsAsync(membership);
        _mockMembershipRepo
            .Setup(x => x.IsGlobalAdminAsync("coach-1"))
            .ReturnsAsync(false);

        _mockSlotRepo
            .Setup(x => x.GetSlotAsync(leagueId, division, "slot-1"))
            .ReturnsAsync(slot);

        SlotQueryFilter? capturedFilter = null;
        _mockSlotRepo
            .Setup(x => x.QuerySlotsAsync(It.IsAny<SlotQueryFilter>(), null))
            .Callback<SlotQueryFilter, string?>((filter, _) => capturedFilter = filter)
            .ReturnsAsync(new PaginationResult<TableEntity>
            {
                Items = new List<TableEntity>(),
                PageSize = 100
            });

        _mockSlotRepo
            .Setup(x => x.UpdateSlotAsync(It.IsAny<TableEntity>(), It.IsAny<ETag>()))
            .Returns(Task.CompletedTask);

        _mockRequestRepo
            .Setup(x => x.CreateRequestAsync(It.IsAny<TableEntity>()))
            .Returns(Task.CompletedTask);

        _mockRequestRepo
            .Setup(x => x.GetPendingRequestsForSlotAsync(leagueId, division, "slot-1"))
            .ReturnsAsync(new List<TableEntity>());

        _mockMembershipRepo
            .Setup(x => x.GetLeagueMembershipsAsync(leagueId))
            .ReturnsAsync(new List<TableEntity>());

        // Act
        await _service.CreateRequestAsync(request, context);

        // Assert: Verify the query filter includes BOTH Confirmed and Open
        Assert.NotNull(capturedFilter);
        Assert.NotNull(capturedFilter.Statuses);
        Assert.Contains(Constants.Status.SlotConfirmed, capturedFilter.Statuses);
        Assert.Contains(Constants.Status.SlotOpen, capturedFilter.Statuses);
        Assert.Equal(2, capturedFilter.Statuses.Count);
    }

}
