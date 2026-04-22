using System;
using System.Collections.Generic;
using System.Globalization;
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
/// Tests for lead time validation consistency (72-hour policy).
/// Verifies fix for Issue #8 from SCHEDULING_LOGIC_REVIEW.md
/// </summary>
public class LeadTimeValidationTests
{
    [Fact]
    public async Task PracticeMove_Within72Hours_ReturnsLeadTimeViolation()
    {
        // CRITICAL TEST: Verifies 72h lead time policy for practice moves (Issue #8)
        // Verifies error code changed to LEAD_TIME_VIOLATION

        // Arrange
        var mockPracticeRequestRepo = new Mock<IPracticeRequestRepository>();
        var mockMembershipRepo = new Mock<IMembershipRepository>();
        var mockSlotRepo = new Mock<ISlotRepository>();
        var mockTeamRepo = new Mock<ITeamRepository>();
        var mockLogger = new Mock<ILogger<PracticeRequestService>>();

        var service = new PracticeRequestService(
            mockPracticeRequestRepo.Object,
            mockMembershipRepo.Object,
            mockSlotRepo.Object,
            mockTeamRepo.Object,
            mockLogger.Object);

        var leagueId = "league-1";
        var userId = "coach-1";
        var sourceRequestId = "req-1";
        var targetSlotId = "slot-2";

        // Practice happening in 48 hours (within 72h lead time)
        var futureDate = DateTime.UtcNow.AddHours(48);
        var practiceDate = futureDate.ToString("yyyy-MM-dd");
        var practiceTime = futureDate.ToString("HH:mm");

        var membership = new TableEntity(userId, leagueId)
        {
            { "Role", Constants.Roles.Coach },
            { "Division", "10U" },
            { "TeamId", "Panthers" }
        };

        var sourceRequest = new TableEntity("PRACTICEREQ|league-1", sourceRequestId)
        {
            { "Status", "Approved" },
            { "Division", "10U" },
            { "TeamId", "Panthers" },
            { "SlotId", "slot-1" }
        };

        var sourceSlot = new TableEntity("SLOT|league-1|10U", "slot-1")
        {
            { "GameDate", practiceDate },
            { "StartTime", practiceTime },
            { "EndTime", futureDate.AddMinutes(90).ToString("HH:mm") }
        };

        mockMembershipRepo
            .Setup(x => x.GetMembershipAsync(userId, leagueId))
            .ReturnsAsync(membership);
        mockMembershipRepo
            .Setup(x => x.IsGlobalAdminAsync(userId))
            .ReturnsAsync(false);

        mockPracticeRequestRepo
            .Setup(x => x.GetRequestAsync(leagueId, sourceRequestId))
            .ReturnsAsync(sourceRequest);

        mockSlotRepo
            .Setup(x => x.GetSlotAsync(leagueId, "10U", "slot-1"))
            .ReturnsAsync(sourceSlot);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ApiGuards.HttpError>(() =>
            service.CreateMoveRequestAsync(leagueId, userId, sourceRequestId, targetSlotId, "reason"));

        Assert.Equal(409, ex.Status);
        Assert.Equal(ErrorCodes.LEAD_TIME_VIOLATION, ex.Code);  // New error code!
        Assert.Contains("72 hours", ex.Message);  // Updated from 48 hours
        Assert.Contains("48", ex.Message);  // Shows hours until practice
    }

    [Fact]
    public async Task PracticeMove_Exactly72Hours_Blocked()
    {
        // TEST: Verifies 72h is the boundary (not 72h+ or 73h+)
        // Exactly 72.0 hours should be blocked

        // Arrange
        var mockPracticeRequestRepo = new Mock<IPracticeRequestRepository>();
        var mockMembershipRepo = new Mock<IMembershipRepository>();
        var mockSlotRepo = new Mock<ISlotRepository>();
        var mockTeamRepo = new Mock<ITeamRepository>();
        var mockLogger = new Mock<ILogger<PracticeRequestService>>();

        var service = new PracticeRequestService(
            mockPracticeRequestRepo.Object,
            mockMembershipRepo.Object,
            mockSlotRepo.Object,
            mockTeamRepo.Object,
            mockLogger.Object);

        // Practice happening in exactly 72 hours
        var futureDate = DateTime.UtcNow.AddHours(72);
        var practiceDate = futureDate.ToString("yyyy-MM-dd");
        var practiceTime = futureDate.ToString("HH:mm");

        var membership = new TableEntity("coach-1", "league-1")
        {
            { "Role", Constants.Roles.Coach },
            { "Division", "10U" },
            { "TeamId", "Panthers" }
        };

        var sourceRequest = new TableEntity("PRACTICEREQ|league-1", "req-1")
        {
            { "Status", "Approved" },
            { "Division", "10U" },
            { "TeamId", "Panthers" },
            { "SlotId", "slot-1" }
        };

        var sourceSlot = new TableEntity("SLOT|league-1|10U", "slot-1")
        {
            { "GameDate", practiceDate },
            { "StartTime", practiceTime }
        };

        mockMembershipRepo
            .Setup(x => x.GetMembershipAsync("coach-1", "league-1"))
            .ReturnsAsync(membership);
        mockMembershipRepo
            .Setup(x => x.IsGlobalAdminAsync("coach-1"))
            .ReturnsAsync(false);

        mockPracticeRequestRepo
            .Setup(x => x.GetRequestAsync("league-1", "req-1"))
            .ReturnsAsync(sourceRequest);

        mockSlotRepo
            .Setup(x => x.GetSlotAsync("league-1", "10U", "slot-1"))
            .ReturnsAsync(sourceSlot);

        // Act & Assert - should be blocked (72h is not "more than 72h")
        var ex = await Assert.ThrowsAsync<ApiGuards.HttpError>(() =>
            service.CreateMoveRequestAsync("league-1", "coach-1", "req-1", "slot-2", "reason"));

        Assert.Equal(ErrorCodes.LEAD_TIME_VIOLATION, ex.Code);
        Assert.Contains("72", ex.Message);
    }

    [Fact]
    public async Task PracticeMove_73HoursAway_Allowed()
    {
        // TEST: Verifies 73h+ is allowed (outside 72h window)

        // Arrange
        var mockPracticeRequestRepo = new Mock<IPracticeRequestRepository>();
        var mockMembershipRepo = new Mock<IMembershipRepository>();
        var mockSlotRepo = new Mock<ISlotRepository>();
        var mockTeamRepo = new Mock<ITeamRepository>();
        var mockLogger = new Mock<ILogger<PracticeRequestService>>();

        var service = new PracticeRequestService(
            mockPracticeRequestRepo.Object,
            mockMembershipRepo.Object,
            mockSlotRepo.Object,
            mockTeamRepo.Object,
            mockLogger.Object);

        // Practice happening in 73 hours (outside lead time)
        var futureDate = DateTime.UtcNow.AddHours(73);
        var practiceDate = futureDate.ToString("yyyy-MM-dd");
        var practiceTime = futureDate.ToString("HH:mm");

        var membership = new TableEntity("coach-1", "league-1")
        {
            { "Role", Constants.Roles.Coach },
            { "Division", "10U" },
            { "TeamId", "Panthers" }
        };

        var sourceRequest = new TableEntity("PRACTICEREQ|league-1", "req-1")
        {
            { "Status", "Approved" },
            { "Division", "10U" },
            { "TeamId", "Panthers" },
            { "SlotId", "slot-1" }
        };

        var sourceSlot = new TableEntity("SLOT|league-1|10U", "slot-1")
        {
            { "GameDate", practiceDate },
            { "StartTime", practiceTime },
            { "EndTime", futureDate.AddMinutes(90).ToString("HH:mm") }
        };

        var targetSlot = new TableEntity("SLOT|league-1|10U", "slot-2")
        {
            { "Status", Constants.Status.SlotOpen },
            { "IsAvailability", true },
            { "GameDate", practiceDate },
            { "StartTime", practiceTime }
        };

        var team = new TableEntity("TEAM|league-1|10U", "Panthers")
        {
            { "Name", "Panthers" }
        };

        mockMembershipRepo
            .Setup(x => x.GetMembershipAsync("coach-1", "league-1"))
            .ReturnsAsync(membership);
        mockMembershipRepo
            .Setup(x => x.IsGlobalAdminAsync("coach-1"))
            .ReturnsAsync(false);

        mockPracticeRequestRepo
            .Setup(x => x.GetRequestAsync("league-1", "req-1"))
            .ReturnsAsync(sourceRequest);

        mockSlotRepo
            .Setup(x => x.GetSlotAsync("league-1", "10U", "slot-1"))
            .ReturnsAsync(sourceSlot);

        mockSlotRepo
            .Setup(x => x.GetSlotAsync("league-1", "10U", "slot-2"))
            .ReturnsAsync(targetSlot);

        mockTeamRepo
            .Setup(x => x.GetTeamAsync("league-1", "10U", "Panthers"))
            .ReturnsAsync(team);

        mockPracticeRequestRepo
            .Setup(x => x.QueryRequestsAsync("league-1", null, "10U", "Panthers", null))
            .ReturnsAsync(new List<TableEntity> { sourceRequest });

        mockPracticeRequestRepo
            .Setup(x => x.QuerySlotRequestsAsync("league-1", "10U", "slot-2", It.IsAny<IReadOnlyCollection<string>>()))
            .ReturnsAsync(new List<TableEntity>());

        mockSlotRepo
            .Setup(x => x.UpdateSlotAsync(It.IsAny<TableEntity>(), It.IsAny<ETag>()))
            .Returns(Task.CompletedTask);

        mockPracticeRequestRepo
            .Setup(x => x.CreateRequestAsync(It.IsAny<TableEntity>()))
            .Returns(Task.CompletedTask);

        // Act - should succeed (73h is outside 72h window)
        var result = await service.CreateMoveRequestAsync("league-1", "coach-1", "req-1", "slot-2", "reason");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Move", result.GetString("RequestKind"));

        // Verify request was created (lead time check passed)
        mockPracticeRequestRepo.Verify(x => x.CreateRequestAsync(It.IsAny<TableEntity>()), Times.Once);
    }

    [Fact]
    public async Task GameReschedule_72HourPolicy_ConsistentWithPracticeMove()
    {
        // TEST: Verifies game reschedule uses same 72h policy as practice move
        // Ensures consistency (Issue #8)

        // Arrange
        var mockRequestRepo = new Mock<IGameRescheduleRequestRepository>();
        var mockSlotRepo = new Mock<ISlotRepository>();
        var mockMembershipRepo = new Mock<IMembershipRepository>();
        var mockNotificationService = new Mock<INotificationService>();
        var mockLogger = new Mock<ILogger<GameRescheduleRequestService>>();

        var service = new GameRescheduleRequestService(
            mockRequestRepo.Object,
            mockSlotRepo.Object,
            mockMembershipRepo.Object,
            mockNotificationService.Object,
            mockLogger.Object);

        var leagueId = "league-1";
        var userId = "coach-1";
        var division = "10U";
        var originalSlotId = "original-slot";
        var proposedSlotId = "proposed-slot";

        // Game happening in 48 hours (within 72h lead time)
        var futureDate = DateTime.UtcNow.AddHours(48);
        var gameDate = futureDate.ToString("yyyy-MM-dd");
        var gameTime = futureDate.ToString("HH:mm");

        var membership = new TableEntity(userId, leagueId)
        {
            { "Role", Constants.Roles.Coach },
            { "TeamId", "Tigers" }
        };

        var originalSlot = new TableEntity($"SLOT|{leagueId}|{division}", originalSlotId)
        {
            { "Status", Constants.Status.SlotConfirmed },
            { "HomeTeamId", "Tigers" },
            { "AwayTeamId", "Lions" },
            { "GameDate", gameDate },
            { "StartTime", gameTime },
            { "EndTime", futureDate.AddMinutes(90).ToString("HH:mm") },
            { "IsAvailability", false }
        };

        var proposedSlot = new TableEntity($"SLOT|{leagueId}|{division}", proposedSlotId)
        {
            { "Status", Constants.Status.SlotOpen },
            { "GameDate", DateTime.UtcNow.AddDays(7).ToString("yyyy-MM-dd") },
            { "StartTime", "18:00" }
        };

        mockMembershipRepo
            .Setup(x => x.GetMembershipAsync(userId, leagueId))
            .ReturnsAsync(membership);
        mockMembershipRepo
            .Setup(x => x.IsGlobalAdminAsync(userId))
            .ReturnsAsync(false);

        mockSlotRepo
            .Setup(x => x.GetSlotAsync(leagueId, division, originalSlotId))
            .ReturnsAsync(originalSlot);

        mockSlotRepo
            .Setup(x => x.GetSlotAsync(leagueId, division, proposedSlotId))
            .ReturnsAsync(proposedSlot);

        mockRequestRepo
            .Setup(x => x.HasActiveRequestForSlotAsync(leagueId, division, originalSlotId, It.IsAny<string[]>()))
            .ReturnsAsync(false);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ApiGuards.HttpError>(() =>
            service.CreateRescheduleRequestAsync(leagueId, userId, division, originalSlotId, proposedSlotId, "Weather"));

        Assert.Contains("72", ex.Message);  // Should mention 72 hours, not 48
    }

    [Fact]
    public async Task LeadTimeValidation_AppliesTo_OriginalTime_NotNewTime()
    {
        // TEST: Clarifies that lead time applies to ORIGINAL practice time
        // Important: Can't move a practice happening in 24h to next week (still violates lead time)

        // Arrange
        var mockPracticeRequestRepo = new Mock<IPracticeRequestRepository>();
        var mockMembershipRepo = new Mock<IMembershipRepository>();
        var mockSlotRepo = new Mock<ISlotRepository>();
        var mockTeamRepo = new Mock<ITeamRepository>();
        var mockLogger = new Mock<ILogger<PracticeRequestService>>();

        var service = new PracticeRequestService(
            mockPracticeRequestRepo.Object,
            mockMembershipRepo.Object,
            mockSlotRepo.Object,
            mockTeamRepo.Object,
            mockLogger.Object);

        var leagueId = "league-1";
        var userId = "coach-1";

        // ORIGINAL practice: 24 hours away (violates 72h lead time)
        var originalPracticeDate = DateTime.UtcNow.AddHours(24);

        // NEW practice: 7 days away (would be OK if this was what we checked)
        var newPracticeDate = DateTime.UtcNow.AddDays(7);

        var membership = new TableEntity(userId, leagueId)
        {
            { "Role", Constants.Roles.Coach },
            { "Division", "10U" },
            { "TeamId", "Panthers" }
        };

        var sourceRequest = new TableEntity("PRACTICEREQ|league-1", "req-1")
        {
            { "Status", "Approved" },
            { "Division", "10U" },
            { "TeamId", "Panthers" },
            { "SlotId", "slot-original" }
        };

        var originalSlot = new TableEntity("SLOT|league-1|10U", "slot-original")
        {
            { "GameDate", originalPracticeDate.ToString("yyyy-MM-dd") },
            { "StartTime", originalPracticeDate.ToString("HH:mm") },  // 24h away
            { "EndTime", originalPracticeDate.AddMinutes(90).ToString("HH:mm") }
        };

        var targetSlot = new TableEntity("SLOT|league-1|10U", "slot-new")
        {
            { "Status", Constants.Status.SlotOpen },
            { "GameDate", newPracticeDate.ToString("yyyy-MM-dd") },  // 7 days away
            { "StartTime", newPracticeDate.ToString("HH:mm") }
        };

        mockMembershipRepo
            .Setup(x => x.GetMembershipAsync(userId, leagueId))
            .ReturnsAsync(membership);
        mockMembershipRepo
            .Setup(x => x.IsGlobalAdminAsync(userId))
            .ReturnsAsync(false);

        mockPracticeRequestRepo
            .Setup(x => x.GetRequestAsync(leagueId, "req-1"))
            .ReturnsAsync(sourceRequest);

        mockSlotRepo
            .Setup(x => x.GetSlotAsync(leagueId, "10U", "slot-original"))
            .ReturnsAsync(originalSlot);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ApiGuards.HttpError>(() =>
            service.CreateMoveRequestAsync(leagueId, userId, "req-1", "slot-new", "reason"));

        // Should be blocked even though NEW time is 7 days away
        // Because ORIGINAL time is only 24h away
        Assert.Equal(ErrorCodes.LEAD_TIME_VIOLATION, ex.Code);
        Assert.Contains("24", ex.Message);  // Shows time until ORIGINAL practice
    }

    [Fact]
    public async Task LeadTime_PastPractice_Allowed()
    {
        // TEST: Verifies lead time check only applies to future practices
        // If practice was in the past, hoursUntilPractice is negative - should not block

        // Arrange
        var mockPracticeRequestRepo = new Mock<IPracticeRequestRepository>();
        var mockMembershipRepo = new Mock<IMembershipRepository>();
        var mockSlotRepo = new Mock<ISlotRepository>();
        var mockTeamRepo = new Mock<ITeamRepository>();
        var mockLogger = new Mock<ILogger<PracticeRequestService>>();

        var service = new PracticeRequestService(
            mockPracticeRequestRepo.Object,
            mockMembershipRepo.Object,
            mockSlotRepo.Object,
            mockTeamRepo.Object,
            mockLogger.Object);

        var leagueId = "league-1";
        var userId = "coach-1";

        // ORIGINAL practice was YESTERDAY (past)
        var pastDate = DateTime.UtcNow.AddHours(-24);
        var pastDateStr = pastDate.ToString("yyyy-MM-dd");
        var pastTimeStr = pastDate.ToString("HH:mm");

        var membership = new TableEntity(userId, leagueId)
        {
            { "Role", Constants.Roles.Coach },
            { "Division", "10U" },
            { "TeamId", "Panthers" }
        };

        var sourceRequest = new TableEntity("PRACTICEREQ|league-1", "req-1")
        {
            { "Status", "Approved" },
            { "Division", "10U" },
            { "TeamId", "Panthers" },
            { "SlotId", "slot-past" }
        };

        var pastSlot = new TableEntity("SLOT|league-1|10U", "slot-past")
        {
            { "GameDate", pastDateStr },
            { "StartTime", pastTimeStr },
            { "EndTime", pastDate.AddMinutes(90).ToString("HH:mm") }
        };

        var targetSlot = new TableEntity("SLOT|league-1|10U", "slot-future")
        {
            { "Status", Constants.Status.SlotOpen },
            { "IsAvailability", true },
            { "GameDate", DateTime.UtcNow.AddDays(7).ToString("yyyy-MM-dd") },
            { "StartTime", "18:00" }
        };

        var team = new TableEntity("TEAM|league-1|10U", "Panthers");

        mockMembershipRepo.Setup(x => x.GetMembershipAsync(userId, leagueId)).ReturnsAsync(membership);
        mockMembershipRepo.Setup(x => x.IsGlobalAdminAsync(userId)).ReturnsAsync(false);
        mockPracticeRequestRepo.Setup(x => x.GetRequestAsync(leagueId, "req-1")).ReturnsAsync(sourceRequest);
        mockSlotRepo.Setup(x => x.GetSlotAsync(leagueId, "10U", "slot-past")).ReturnsAsync(pastSlot);
        mockSlotRepo.Setup(x => x.GetSlotAsync(leagueId, "10U", "slot-future")).ReturnsAsync(targetSlot);
        mockTeamRepo.Setup(x => x.GetTeamAsync(leagueId, "10U", "Panthers")).ReturnsAsync(team);
        mockPracticeRequestRepo.Setup(x => x.QueryRequestsAsync(leagueId, null, "10U", "Panthers", null))
            .ReturnsAsync(new List<TableEntity> { sourceRequest });
        mockPracticeRequestRepo.Setup(x => x.QuerySlotRequestsAsync(leagueId, "10U", "slot-future", It.IsAny<IReadOnlyCollection<string>>()))
            .ReturnsAsync(new List<TableEntity>());
        mockSlotRepo.Setup(x => x.UpdateSlotAsync(It.IsAny<TableEntity>(), It.IsAny<ETag>())).Returns(Task.CompletedTask);
        mockPracticeRequestRepo.Setup(x => x.CreateRequestAsync(It.IsAny<TableEntity>())).Returns(Task.CompletedTask);

        // Act - should succeed (past practice, hoursUntilPractice is negative)
        var result = await service.CreateMoveRequestAsync(leagueId, userId, "req-1", "slot-future", "reason");

        // Assert
        Assert.NotNull(result);
        mockPracticeRequestRepo.Verify(x => x.CreateRequestAsync(It.IsAny<TableEntity>()), Times.Once);
    }
}
