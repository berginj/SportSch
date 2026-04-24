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

/// <summary>
/// Tests for umpire assignment service - CRITICAL for conflict detection and double-booking prevention.
/// </summary>
public class UmpireAssignmentServiceTests
{
    private readonly Mock<IGameUmpireAssignmentRepository> _mockAssignmentRepo;
    private readonly Mock<IUmpireProfileRepository> _mockUmpireRepo;
    private readonly Mock<ISlotRepository> _mockSlotRepo;
    private readonly Mock<IMembershipRepository> _mockMembershipRepo;
    private readonly Mock<INotificationService> _mockNotificationService;
    private readonly Mock<UmpireNotificationService> _mockUmpireNotificationService;
    private readonly Mock<ILogger<UmpireAssignmentService>> _mockLogger;
    private readonly UmpireAssignmentService _service;

    public UmpireAssignmentServiceTests()
    {
        _mockAssignmentRepo = new Mock<IGameUmpireAssignmentRepository>();
        _mockUmpireRepo = new Mock<IUmpireProfileRepository>();
        _mockSlotRepo = new Mock<ISlotRepository>();
        _mockMembershipRepo = new Mock<IMembershipRepository>();
        _mockNotificationService = new Mock<INotificationService>();
        _mockUmpireNotificationService = new Mock<UmpireNotificationService>(
            Mock.Of<IEmailService>(),
            Mock.Of<INotificationService>(),
            Mock.Of<IUmpireProfileRepository>(),
            Mock.Of<ILogger<UmpireNotificationService>>());
        _mockLogger = new Mock<ILogger<UmpireAssignmentService>>();

        _service = new UmpireAssignmentService(
            _mockAssignmentRepo.Object,
            _mockUmpireRepo.Object,
            _mockSlotRepo.Object,
            _mockMembershipRepo.Object,
            _mockNotificationService.Object,
            _mockUmpireNotificationService.Object,
            _mockLogger.Object);
    }

    [Fact]
    public async Task AssignUmpire_Success_CreatesAssignmentAndNotifies()
    {
        // Arrange
        var request = new AssignUmpireRequest
        {
            LeagueId = "league-1",
            Division = "10U",
            SlotId = "slot-1",
            UmpireUserId = "umpire-1",
            SendNotification = true
        };

        var context = new CorrelationContext
        {
            UserId = "admin-1",
            UserEmail = "admin@example.com",
            LeagueId = "league-1"
        };

        var umpire = new TableEntity("UMPIRE|league-1", "umpire-1")
        {
            ["Name"] = "John Doe",
            ["IsActive"] = true,
            ["Email"] = "umpire@example.com"
        };

        var game = new TableEntity("SLOT|league-1|10U", "slot-1")
        {
            ["GameDate"] = "2026-06-15",
            ["StartTime"] = "15:00",
            ["EndTime"] = "16:30",
            ["StartMin"] = 900,
            ["EndMin"] = 990,
            ["FieldKey"] = "park1/field1",
            ["DisplayName"] = "Park 1 > Field 1",
            ["HomeTeamId"] = "Tigers",
            ["AwayTeamId"] = "Lions"
        };

        _mockUmpireRepo.Setup(x => x.GetUmpireAsync("league-1", "umpire-1")).ReturnsAsync(umpire);
        _mockSlotRepo.Setup(x => x.GetSlotAsync("league-1", "10U", "slot-1")).ReturnsAsync(game);
        _mockAssignmentRepo.Setup(x => x.GetAssignmentByGameAndUmpireAsync("league-1", "10U", "slot-1", "umpire-1")).ReturnsAsync((TableEntity?)null);
        _mockAssignmentRepo.Setup(x => x.GetAssignmentsByUmpireAndDateAsync("league-1", "umpire-1", "2026-06-15")).ReturnsAsync(new List<TableEntity>());
        _mockAssignmentRepo.Setup(x => x.CreateAssignmentAsync(It.IsAny<TableEntity>())).Returns(Task.CompletedTask);

        // Act
        var result = await _service.AssignUmpireToGameAsync(request, context);

        // Assert
        Assert.NotNull(result);
        _mockAssignmentRepo.Verify(x => x.CreateAssignmentAsync(
            It.Is<TableEntity>(a =>
                a.GetString("UmpireUserId") == "umpire-1" &&
                a.GetString("SlotId") == "slot-1" &&
                a.GetString("Status") == "Assigned")),
            Times.Once);
    }

    [Fact]
    public async Task AssignUmpire_UmpireHasConflict_ThrowsUmpireConflictError()
    {
        // CRITICAL TEST: Validates conflict detection prevents double-booking

        // Arrange
        var request = new AssignUmpireRequest
        {
            LeagueId = "league-1",
            Division = "10U",
            SlotId = "slot-new",
            UmpireUserId = "umpire-1"
        };

        var context = new CorrelationContext { UserId = "admin-1", LeagueId = "league-1" };

        var umpire = new TableEntity("UMPIRE|league-1", "umpire-1")
        {
            ["IsActive"] = true
        };

        var newGame = new TableEntity("SLOT|league-1|10U", "slot-new")
        {
            ["GameDate"] = "2026-06-15",
            ["StartTime"] = "15:30",  // 3:30pm
            ["EndTime"] = "17:00",    // 5:00pm
            ["StartMin"] = 930,
            ["EndMin"] = 1020
        };

        // Existing assignment: 3:00pm-5:00pm (overlaps with 3:30-5:00)
        var existingAssignment = new TableEntity("UMPASSIGN|league-1|10U|slot-existing", "assign-1")
        {
            ["UmpireUserId"] = "umpire-1",
            ["Status"] = "Accepted",
            ["GameDate"] = "2026-06-15",
            ["StartTime"] = "15:00",
            ["EndTime"] = "17:00",
            ["StartMin"] = 900,
            ["EndMin"] = 1020,
            ["FieldDisplayName"] = "Park 2 > Field 3",
            ["HomeTeamId"] = "Panthers",
            ["AwayTeamId"] = "Bears"
        };

        _mockUmpireRepo.Setup(x => x.GetUmpireAsync("league-1", "umpire-1")).ReturnsAsync(umpire);
        _mockSlotRepo.Setup(x => x.GetSlotAsync("league-1", "10U", "slot-new")).ReturnsAsync(newGame);
        _mockAssignmentRepo.Setup(x => x.GetAssignmentByGameAndUmpireAsync("league-1", "10U", "slot-new", "umpire-1")).ReturnsAsync((TableEntity?)null);
        _mockAssignmentRepo.Setup(x => x.GetAssignmentsByUmpireAndDateAsync("league-1", "umpire-1", "2026-06-15"))
            .ReturnsAsync(new List<TableEntity> { existingAssignment });

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ApiGuards.HttpError>(() =>
            _service.AssignUmpireToGameAsync(request, context));

        Assert.Equal(409, ex.Status);
        Assert.Equal(ErrorCodes.UMPIRE_CONFLICT, ex.Code);
        Assert.Contains("conflicting assignment", ex.Message.ToLower());

        // Verify assignment was NEVER created (conflict prevented it)
        _mockAssignmentRepo.Verify(x => x.CreateAssignmentAsync(It.IsAny<TableEntity>()), Times.Never);
    }

    [Fact]
    public async Task AssignUmpire_InactiveUmpire_ThrowsUmpireInactiveError()
    {
        // Arrange
        var request = new AssignUmpireRequest
        {
            LeagueId = "league-1",
            Division = "10U",
            SlotId = "slot-1",
            UmpireUserId = "umpire-inactive"
        };

        var context = new CorrelationContext { UserId = "admin-1", LeagueId = "league-1" };

        var inactiveUmpire = new TableEntity("UMPIRE|league-1", "umpire-inactive")
        {
            ["IsActive"] = false  // Inactive!
        };

        _mockUmpireRepo.Setup(x => x.GetUmpireAsync("league-1", "umpire-inactive")).ReturnsAsync(inactiveUmpire);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ApiGuards.HttpError>(() =>
            _service.AssignUmpireToGameAsync(request, context));

        Assert.Equal(400, ex.Status);
        Assert.Equal(ErrorCodes.UMPIRE_INACTIVE, ex.Code);

        // Verify no assignment created
        _mockAssignmentRepo.Verify(x => x.CreateAssignmentAsync(It.IsAny<TableEntity>()), Times.Never);
    }

    [Fact]
    public async Task CheckUmpireConflicts_NoOverlap_ReturnsEmpty()
    {
        // TEST: Touching boundaries don't conflict (3pm-5pm and 5pm-7pm)

        // Arrange
        var existingAssignment = new TableEntity("UMPASSIGN|league-1|10U|slot-1", "assign-1")
        {
            ["GameDate"] = "2026-06-15",
            ["StartTime"] = "15:00",
            ["EndTime"] = "17:00",  // Ends at 5pm
            ["StartMin"] = 900,
            ["EndMin"] = 1020,
            ["Status"] = "Accepted"
        };

        _mockAssignmentRepo.Setup(x => x.GetAssignmentsByUmpireAndDateAsync("league-1", "umpire-1", "2026-06-15"))
            .ReturnsAsync(new List<TableEntity> { existingAssignment });

        // Act: Check for conflict with 5pm-7pm game (starts exactly when other ends)
        var conflicts = await _service.CheckUmpireConflictsAsync(
            "league-1",
            "umpire-1",
            "2026-06-15",
            1020,  // 5:00pm
            1140,  // 7:00pm
            null);

        // Assert: No conflict (touching boundaries are OK)
        Assert.Empty(conflicts);
    }

    [Fact]
    public async Task CheckUmpireConflicts_SkipsDeclinedAssignments()
    {
        // TEST: Declined assignments should not block new assignments

        // Arrange
        var declinedAssignment = new TableEntity("UMPASSIGN|league-1|10U|slot-declined", "assign-declined")
        {
            ["GameDate"] = "2026-06-15",
            ["StartTime"] = "15:00",
            ["EndTime"] = "17:00",
            ["StartMin"] = 900,
            ["EndMin"] = 1020,
            ["Status"] = "Declined"  // Declined status
        };

        _mockAssignmentRepo.Setup(x => x.GetAssignmentsByUmpireAndDateAsync("league-1", "umpire-1", "2026-06-15"))
            .ReturnsAsync(new List<TableEntity> { declinedAssignment });

        // Act: Try to assign 3:30-5:00 (overlaps with declined assignment)
        var conflicts = await _service.CheckUmpireConflictsAsync(
            "league-1",
            "umpire-1",
            "2026-06-15",
            930,   // 3:30pm
            1020,  // 5:00pm
            null);

        // Assert: No conflict (declined assignments ignored)
        Assert.Empty(conflicts);
    }

    [Fact]
    public async Task UpdateAssignmentStatus_UmpireAccepts_NotifiesAdmin()
    {
        // Arrange
        var assignmentId = "assign-1";
        var assignment = new TableEntity("UMPASSIGN|league-1|10U|slot-1", assignmentId)
        {
            ["UmpireUserId"] = "umpire-1",
            ["Status"] = "Assigned",
            ["LeagueId"] = "league-1",
            ["HomeTeamId"] = "Tigers",
            ["AwayTeamId"] = "Lions",
            ["GameDate"] = "2026-06-15"
        };
        assignment.ETag = new ETag("etag-1");

        var context = new CorrelationContext
        {
            UserId = "umpire-1",
            LeagueId = "league-1"
        };

        var adminMembership = new TableEntity("admin-1", "league-1")
        {
            ["Role"] = Constants.Roles.LeagueAdmin
        };

        _mockAssignmentRepo.Setup(x => x.GetAssignmentAsync("league-1", assignmentId)).ReturnsAsync(assignment);
        _mockMembershipRepo.Setup(x => x.IsGlobalAdminAsync("umpire-1")).ReturnsAsync(false);
        _mockMembershipRepo.Setup(x => x.GetLeagueMembershipsAsync("league-1"))
            .ReturnsAsync(new List<TableEntity> { adminMembership });
        _mockAssignmentRepo.Setup(x => x.UpdateAssignmentAsync(It.IsAny<TableEntity>(), It.IsAny<ETag>())).Returns(Task.CompletedTask);
        _mockNotificationService.Setup(x => x.CreateNotificationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync("notif-1");

        // Act
        var result = await _service.UpdateAssignmentStatusAsync(assignmentId, "Accepted", null, context);

        // Assert
        _mockAssignmentRepo.Verify(x => x.UpdateAssignmentAsync(
            It.Is<TableEntity>(a => a.GetString("Status") == "Accepted"),
            It.IsAny<ETag>()),
            Times.Once);

        // Verify admin was notified
        _mockNotificationService.Verify(x => x.CreateNotificationAsync(
            "admin-1",
            "league-1",
            "UmpireAccepted",
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>()),
            Times.Once);
    }

    [Fact]
    public async Task UpdateAssignmentStatus_UmpireCannotSetCancelled()
    {
        // TEST: Umpires can only Accept or Decline, not Cancel

        // Arrange
        var assignmentId = "assign-1";
        var assignment = new TableEntity("UMPASSIGN|league-1|10U|slot-1", assignmentId)
        {
            ["UmpireUserId"] = "umpire-1",
            ["Status"] = "Assigned",
            ["LeagueId"] = "league-1"
        };

        var context = new CorrelationContext
        {
            UserId = "umpire-1",  // Umpire trying to cancel
            LeagueId = "league-1"
        };

        _mockAssignmentRepo.Setup(x => x.GetAssignmentAsync("league-1", assignmentId)).ReturnsAsync(assignment);
        _mockMembershipRepo.Setup(x => x.IsGlobalAdminAsync("umpire-1")).ReturnsAsync(false);
        _mockMembershipRepo.Setup(x => x.GetMembershipAsync("umpire-1", "league-1")).ReturnsAsync((TableEntity?)null);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ApiGuards.HttpError>(() =>
            _service.UpdateAssignmentStatusAsync(assignmentId, "Cancelled", null, context));

        Assert.Equal(403, ex.Status);
        Assert.Equal(ErrorCodes.FORBIDDEN, ex.Code);
        Assert.Contains("can only accept or decline", ex.Message.ToLower());
    }

    [Fact]
    public async Task CheckUmpireConflicts_MultipleGamesOnSameDay_DetectsOverlaps()
    {
        // TEST: Umpire with multiple games on same day - detect overlaps correctly

        // Arrange
        var assignment1 = new TableEntity("UMPASSIGN|league-1|10U|slot-1", "assign-1")
        {
            ["GameDate"] = "2026-06-15",
            ["StartMin"] = 900,   // 3:00pm
            ["EndMin"] = 1020,    // 5:00pm
            ["Status"] = "Accepted",
            ["StartTime"] = "15:00",
            ["EndTime"] = "17:00",
            ["FieldDisplayName"] = "Field 1"
        };

        var assignment2 = new TableEntity("UMPASSIGN|league-1|12U|slot-2", "assign-2")
        {
            ["GameDate"] = "2026-06-15",
            ["StartMin"] = 1080,  // 6:00pm
            ["EndMin"] = 1200,    // 8:00pm
            ["Status"] = "Accepted",
            ["StartTime"] = "18:00",
            ["EndTime"] = "20:00",
            ["FieldDisplayName"] = "Field 2"
        };

        _mockAssignmentRepo.Setup(x => x.GetAssignmentsByUmpireAndDateAsync("league-1", "umpire-1", "2026-06-15"))
            .ReturnsAsync(new List<TableEntity> { assignment1, assignment2 });

        // Act: Check for conflict with 4:00-6:30pm (overlaps with first game)
        var conflicts = await _service.CheckUmpireConflictsAsync(
            "league-1",
            "umpire-1",
            "2026-06-15",
            960,   // 4:00pm
            1110,  // 6:30pm
            null);

        // Assert: Should detect conflict with first game only
        Assert.Single(conflicts);
        var conflict = conflicts[0] as dynamic;
        Assert.Equal("15:00", conflict.startTime);
        Assert.Equal("Field 1", conflict.field);
    }

    [Fact]
    public async Task RemoveAssignment_NonAdmin_ThrowsForbidden()
    {
        // TEST: Only admins can remove assignments

        // Arrange
        var assignmentId = "assign-1";
        var assignment = new TableEntity("UMPASSIGN|league-1|10U|slot-1", assignmentId)
        {
            ["LeagueId"] = "league-1",
            ["UmpireUserId"] = "umpire-1"
        };

        var context = new CorrelationContext
        {
            UserId = "umpire-1",  // Umpire trying to remove (not admin)
            LeagueId = "league-1"
        };

        _mockAssignmentRepo.Setup(x => x.GetAssignmentAsync("league-1", assignmentId)).ReturnsAsync(assignment);
        _mockMembershipRepo.Setup(x => x.IsGlobalAdminAsync("umpire-1")).ReturnsAsync(false);
        _mockMembershipRepo.Setup(x => x.GetMembershipAsync("umpire-1", "league-1")).ReturnsAsync((TableEntity?)null);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ApiGuards.HttpError>(() =>
            _service.RemoveAssignmentAsync(assignmentId, context));

        Assert.Equal(403, ex.Status);
        Assert.Equal(ErrorCodes.FORBIDDEN, ex.Code);
    }

    [Fact]
    public async Task GetUnassignedGames_FiltersGamesWithoutActiveAssignments()
    {
        // TEST: Unassigned games list includes games with no umpire or declined assignments

        // Arrange
        var game1 = new TableEntity("SLOT|league-1|10U", "slot-1")
        {
            ["GameDate"] = "2026-06-15",
            ["Division"] = "10U",
            ["HomeTeamId"] = "Tigers",
            ["AwayTeamId"] = "Lions"
        };

        var game2 = new TableEntity("SLOT|league-1|10U", "slot-2")
        {
            ["GameDate"] = "2026-06-16",
            ["Division"] = "10U",
            ["HomeTeamId"] = "Panthers",
            ["AwayTeamId"] = "Bears"
        };

        var game3 = new TableEntity("SLOT|league-1|10U", "slot-3")  // Has active assignment
        {
            ["GameDate"] = "2026-06-17",
            ["Division"] = "10U"
        };

        _mockSlotRepo.Setup(x => x.QuerySlotsAsync(It.IsAny<SlotQueryFilter>(), null))
            .ReturnsAsync(new PaginationResult<TableEntity>
            {
                Items = new List<TableEntity> { game1, game2, game3 },
                PageSize = 100
            });

        _mockAssignmentRepo.Setup(x => x.GetAssignmentsByGameAsync("league-1", "10U", "slot-1"))
            .ReturnsAsync(new List<TableEntity>());  // No assignment

        var declinedAssignment = new TableEntity("UMPASSIGN|league-1|10U|slot-2", "assign-2")
        {
            ["Status"] = "Declined"
        };
        _mockAssignmentRepo.Setup(x => x.GetAssignmentsByGameAsync("league-1", "10U", "slot-2"))
            .ReturnsAsync(new List<TableEntity> { declinedAssignment });  // Declined assignment

        var activeAssignment = new TableEntity("UMPASSIGN|league-1|10U|slot-3", "assign-3")
        {
            ["Status"] = "Accepted"
        };
        _mockAssignmentRepo.Setup(x => x.GetAssignmentsByGameAsync("league-1", "10U", "slot-3"))
            .ReturnsAsync(new List<TableEntity> { activeAssignment });  // Active assignment

        // Act
        var result = await _service.GetUnassignedGamesAsync("league-1", new UnassignedGamesFilter());

        // Assert: Should return slot-1 (no assignment) and slot-2 (declined), but NOT slot-3 (accepted)
        Assert.Equal(2, result.Count);
    }
}
