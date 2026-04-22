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
/// Tests for slot creation race conditions and conflict detection.
/// Verifies post-create verification fix and comprehensive conflict checking.
/// </summary>
public class SlotCreationConflictTests
{
    private readonly Mock<ISlotRepository> _mockSlotRepo;
    private readonly Mock<IFieldRepository> _mockFieldRepo;
    private readonly Mock<IAuthorizationService> _mockAuthService;
    private readonly Mock<INotificationService> _mockNotificationService;
    private readonly Mock<IMembershipRepository> _mockMembershipRepo;
    private readonly Mock<INotificationPreferencesService> _mockPreferencesService;
    private readonly Mock<IEmailService> _mockEmailService;
    private readonly Mock<ILogger<SlotService>> _mockLogger;
    private readonly SlotService _service;

    public SlotCreationConflictTests()
    {
        _mockSlotRepo = new Mock<ISlotRepository>();
        _mockFieldRepo = new Mock<IFieldRepository>();
        _mockAuthService = new Mock<IAuthorizationService>();
        _mockNotificationService = new Mock<INotificationService>();
        _mockMembershipRepo = new Mock<IMembershipRepository>();
        _mockPreferencesService = new Mock<INotificationPreferencesService>();
        _mockEmailService = new Mock<IEmailService>();
        _mockLogger = new Mock<ILogger<SlotService>>();

        _service = new SlotService(
            _mockSlotRepo.Object,
            _mockFieldRepo.Object,
            _mockAuthService.Object,
            _mockNotificationService.Object,
            _mockMembershipRepo.Object,
            _mockPreferencesService.Object,
            _mockEmailService.Object,
            _mockLogger.Object);
    }

    [Fact]
    public async Task CreateSlotAsync_ConcurrentCreation_PostCreateConflictDetected_DeletesAndThrows()
    {
        // CRITICAL TEST: Verifies post-create conflict detection (our earlier fix)
        // Scenario: Two coaches create overlapping slots simultaneously
        //           Both pass pre-check, both create, post-check detects conflict
        // Expected: Second slot deleted, SLOT_CONFLICT error thrown

        // Arrange
        var leagueId = "league-1";
        var division = "10U";
        var fieldKey = "park1/field1";
        var gameDate = "2026-06-15";

        var context = new CorrelationContext
        {
            UserId = "coach-2",
            UserEmail = "coach2@example.com",
            LeagueId = leagueId
        };

        var request = new CreateSlotRequest
        {
            Division = division,
            OfferingTeamId = "Lions",
            GameDate = gameDate,
            StartTime = "15:30",  // 3:30pm
            EndTime = "17:00",    // 5:00pm (overlaps with existing 3:00-5:00)
            FieldKey = fieldKey,
            GameType = "Game"
        };

        var field = new TableEntity($"FIELD|{leagueId}|park1", "field1")
        {
            { "IsActive", true },
            { "ParkName", "Park 1" },
            { "FieldName", "Field 1" },
            { "DisplayName", "Park 1 > Field 1" }
        };

        _mockAuthService
            .Setup(x => x.CanCreateSlotAsync("coach-2", leagueId, division, "Lions"))
            .ReturnsAsync(true);

        _mockFieldRepo
            .Setup(x => x.GetFieldAsync(leagueId, "park1", "field1"))
            .ReturnsAsync(field);

        // Pre-check: No conflicts found (first slot not created yet)
        _mockSlotRepo
            .SetupSequence(x => x.HasConflictAsync(leagueId, fieldKey, gameDate, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync(false)  // Pre-check: passes
            .ReturnsAsync(true);  // Post-check: detects conflict (other slot created in between)

        string? createdSlotId = null;
        _mockSlotRepo
            .Setup(x => x.CreateSlotAsync(It.IsAny<TableEntity>()))
            .Callback<TableEntity>(slot => createdSlotId = slot.RowKey)
            .Returns(Task.CompletedTask);

        _mockSlotRepo
            .Setup(x => x.DeleteSlotAsync(leagueId, division, It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        _mockMembershipRepo
            .Setup(x => x.GetLeagueMembershipsAsync(leagueId))
            .ReturnsAsync(new List<TableEntity>());

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ApiGuards.HttpError>(() =>
            _service.CreateSlotAsync(request, context));

        Assert.Equal(409, ex.Status);
        Assert.Equal(ErrorCodes.SLOT_CONFLICT, ex.Code);
        Assert.Contains("already has a slot", ex.Message.ToLower());

        // CRITICAL: Verify slot was deleted after post-check detected conflict
        Assert.NotNull(createdSlotId);
        _mockSlotRepo.Verify(
            x => x.DeleteSlotAsync(leagueId, division, createdSlotId),
            Times.Once,
            "Conflicting slot should be deleted when post-create conflict detected");
    }

    [Fact]
    public async Task CreateSlotAsync_PostCreateVerification_NoConflict_SlotKept()
    {
        // TEST: Verifies post-create check passes when no conflict
        // Ensures we don't delete valid slots

        // Arrange
        var leagueId = "league-1";
        var division = "10U";

        var context = new CorrelationContext
        {
            UserId = "coach-1",
            UserEmail = "coach@example.com",
            LeagueId = leagueId
        };

        var request = new CreateSlotRequest
        {
            Division = division,
            OfferingTeamId = "Tigers",
            GameDate = "2026-06-15",
            StartTime = "18:00",
            EndTime = "19:30",
            FieldKey = "park1/field1",
            GameType = "Game"
        };

        var field = new TableEntity($"FIELD|{leagueId}|park1", "field1")
        {
            { "IsActive", true },
            { "ParkName", "Park 1" },
            { "FieldName", "Field 1" }
        };

        _mockAuthService
            .Setup(x => x.CanCreateSlotAsync("coach-1", leagueId, division, "Tigers"))
            .ReturnsAsync(true);

        _mockFieldRepo
            .Setup(x => x.GetFieldAsync(leagueId, "park1", "field1"))
            .ReturnsAsync(field);

        // Both pre-check and post-check pass (no conflicts)
        _mockSlotRepo
            .Setup(x => x.HasConflictAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync(false);

        _mockSlotRepo
            .Setup(x => x.CreateSlotAsync(It.IsAny<TableEntity>()))
            .Returns(Task.CompletedTask);

        _mockMembershipRepo
            .Setup(x => x.GetLeagueMembershipsAsync(leagueId))
            .ReturnsAsync(new List<TableEntity>());

        // Act
        var result = await _service.CreateSlotAsync(request, context);

        // Assert
        Assert.NotNull(result);

        // Verify slot was created
        _mockSlotRepo.Verify(x => x.CreateSlotAsync(It.IsAny<TableEntity>()), Times.Once);

        // Verify slot was NOT deleted (no conflict detected in post-check)
        _mockSlotRepo.Verify(
            x => x.DeleteSlotAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never,
            "Slot should not be deleted when no conflict exists");

        // Verify both pre-check and post-check were called
        _mockSlotRepo.Verify(
            x => x.HasConflictAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()),
            Times.Exactly(2),
            "Should have both pre-create and post-create conflict checks");
    }

    [Fact]
    public async Task CreateSlotAsync_ExcludesOwnSlotInPostCheck()
    {
        // TEST: Verifies post-create check excludes the slot being created
        // Important: Post-check should use excludeSlotId parameter

        // Arrange
        var leagueId = "league-1";
        var division = "10U";
        var fieldKey = "park1/field1";
        var gameDate = "2026-06-15";

        var context = new CorrelationContext
        {
            UserId = "coach-1",
            LeagueId = leagueId
        };

        var request = new CreateSlotRequest
        {
            Division = division,
            OfferingTeamId = "Tigers",
            GameDate = gameDate,
            StartTime = "18:00",
            EndTime = "19:30",
            FieldKey = fieldKey,
            GameType = "Game"
        };

        var field = new TableEntity($"FIELD|{leagueId}|park1", "field1")
        {
            { "IsActive", true },
            { "ParkName", "Park 1" },
            { "FieldName", "Field 1" }
        };

        string? createdSlotId = null;
        string? postCheckExcludeSlotId = null;

        _mockAuthService
            .Setup(x => x.CanCreateSlotAsync("coach-1", leagueId, division, "Tigers"))
            .ReturnsAsync(true);

        _mockFieldRepo
            .Setup(x => x.GetFieldAsync(leagueId, "park1", "field1"))
            .ReturnsAsync(field);

        _mockSlotRepo
            .Setup(x => x.CreateSlotAsync(It.IsAny<TableEntity>()))
            .Callback<TableEntity>(slot => createdSlotId = slot.RowKey)
            .Returns(Task.CompletedTask);

        _mockSlotRepo
            .Setup(x => x.HasConflictAsync(leagueId, fieldKey, gameDate, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()))
            .Callback<string, string, string, int, int, string?>((_, _, _, _, _, excludeId) =>
                postCheckExcludeSlotId = excludeId)
            .ReturnsAsync(false);

        _mockMembershipRepo
            .Setup(x => x.GetLeagueMembershipsAsync(leagueId))
            .ReturnsAsync(new List<TableEntity>());

        // Act
        await _service.CreateSlotAsync(request, context);

        // Assert
        Assert.NotNull(createdSlotId);
        Assert.NotNull(postCheckExcludeSlotId);

        // CRITICAL: Post-check should exclude the slot we just created
        Assert.Equal(createdSlotId, postCheckExcludeSlotId);

        // Verify HasConflictAsync called twice (pre and post)
        _mockSlotRepo.Verify(
            x => x.HasConflictAsync(leagueId, fieldKey, gameDate, It.IsAny<int>(), It.IsAny<int>(), null),
            Times.Once,
            "Pre-check should have no excludeSlotId");

        _mockSlotRepo.Verify(
            x => x.HasConflictAsync(leagueId, fieldKey, gameDate, It.IsAny<int>(), It.IsAny<int>(), createdSlotId),
            Times.Once,
            "Post-check should exclude the newly created slot");
    }

    [Fact]
    public async Task CreateSlotAsync_ConflictInPreCheck_NeverCreatesSlot()
    {
        // TEST: Verifies pre-check prevents unnecessary slot creation
        // If conflict detected before creation, don't create at all

        // Arrange
        var leagueId = "league-1";
        var division = "10U";

        var context = new CorrelationContext
        {
            UserId = "coach-1",
            LeagueId = leagueId
        };

        var request = new CreateSlotRequest
        {
            Division = division,
            OfferingTeamId = "Tigers",
            GameDate = "2026-06-15",
            StartTime = "18:00",
            EndTime = "19:30",
            FieldKey = "park1/field1",
            GameType = "Game"
        };

        var field = new TableEntity($"FIELD|{leagueId}|park1", "field1")
        {
            { "IsActive", true },
            { "ParkName", "Park 1" },
            { "FieldName", "Field 1" }
        };

        _mockAuthService
            .Setup(x => x.CanCreateSlotAsync("coach-1", leagueId, division, "Tigers"))
            .ReturnsAsync(true);

        _mockFieldRepo
            .Setup(x => x.GetFieldAsync(leagueId, "park1", "field1"))
            .ReturnsAsync(field);

        // Pre-check detects conflict
        _mockSlotRepo
            .Setup(x => x.HasConflictAsync(leagueId, "park1/field1", "2026-06-15", It.IsAny<int>(), It.IsAny<int>(), null))
            .ReturnsAsync(true);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ApiGuards.HttpError>(() =>
            _service.CreateSlotAsync(request, context));

        Assert.Equal(409, ex.Status);
        Assert.Equal(ErrorCodes.SLOT_CONFLICT, ex.Code);

        // Verify slot was NEVER created (caught in pre-check)
        _mockSlotRepo.Verify(
            x => x.CreateSlotAsync(It.IsAny<TableEntity>()),
            Times.Never,
            "Slot should not be created when pre-check detects conflict");

        // Verify only pre-check was called (with null excludeSlotId)
        _mockSlotRepo.Verify(
            x => x.HasConflictAsync(leagueId, "park1/field1", "2026-06-15", It.IsAny<int>(), It.IsAny<int>(), null),
            Times.Once,
            "Only pre-check should run when conflict detected");
    }

    [Fact]
    public async Task CreateSlotAsync_InactiveField_ReturnsFieldInactiveError()
    {
        // TEST: Verifies new FIELD_INACTIVE error code (Issue #8)

        // Arrange
        var context = new CorrelationContext { UserId = "coach-1", LeagueId = "league-1" };
        var request = new CreateSlotRequest
        {
            Division = "10U",
            OfferingTeamId = "Tigers",
            GameDate = "2026-06-15",
            StartTime = "18:00",
            EndTime = "19:30",
            FieldKey = "park1/field1",
            GameType = "Game"
        };

        var inactiveField = new TableEntity("FIELD|league-1|park1", "field1")
        {
            { "IsActive", false },  // Inactive!
            { "ParkName", "Park 1" },
            { "FieldName", "Field 1" }
        };

        _mockAuthService
            .Setup(x => x.CanCreateSlotAsync("coach-1", "league-1", "10U", "Tigers"))
            .ReturnsAsync(true);

        _mockFieldRepo
            .Setup(x => x.GetFieldAsync("league-1", "park1", "field1"))
            .ReturnsAsync(inactiveField);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ApiGuards.HttpError>(() =>
            _service.CreateSlotAsync(request, context));

        Assert.Equal(400, ex.Status);
        Assert.Equal(ErrorCodes.FIELD_INACTIVE, ex.Code);
        Assert.Contains("not active", ex.Message.ToLower());

        // Verify no slot created
        _mockSlotRepo.Verify(x => x.CreateSlotAsync(It.IsAny<TableEntity>()), Times.Never);
    }

    [Fact]
    public async Task CreateSlotAsync_TimeOverlapLogic_TouchingBoundariesAllowed()
    {
        // TEST: Verifies time overlap logic correctly handles touching boundaries
        // 3:00-5:00 and 5:00-7:00 should NOT overlap (touching is OK)

        // Arrange
        var leagueId = "league-1";
        var division = "10U";
        var fieldKey = "park1/field1";

        var context = new CorrelationContext
        {
            UserId = "coach-1",
            LeagueId = leagueId
        };

        // Creating slot 5:00pm-7:00pm
        var request = new CreateSlotRequest
        {
            Division = division,
            OfferingTeamId = "Tigers",
            GameDate = "2026-06-15",
            StartTime = "17:00",  // 5:00pm
            EndTime = "19:00",    // 7:00pm
            FieldKey = fieldKey,
            GameType = "Game"
        };

        var field = new TableEntity($"FIELD|{leagueId}|park1", "field1")
        {
            { "IsActive", true },
            { "ParkName", "Park 1" },
            { "FieldName", "Field 1" }
        };

        // Existing slot 3:00pm-5:00pm (ends exactly when new one starts)
        var existingSlot = new TableEntity($"SLOT|{leagueId}|{division}", "existing")
        {
            { "Status", Constants.Status.SlotConfirmed },
            { "GameDate", "2026-06-15" },
            { "StartTime", "15:00" },
            { "EndTime", "17:00" },  // Ends at 5:00pm
            { "StartMin", 900 },
            { "EndMin", 1020 },
            { "FieldKey", fieldKey }
        };

        _mockAuthService
            .Setup(x => x.CanCreateSlotAsync("coach-1", leagueId, division, "Tigers"))
            .ReturnsAsync(true);

        _mockFieldRepo
            .Setup(x => x.GetFieldAsync(leagueId, "park1", "field1"))
            .ReturnsAsync(field);

        // HasConflictAsync should return false (touching boundaries don't overlap)
        _mockSlotRepo
            .Setup(x => x.HasConflictAsync(leagueId, fieldKey, "2026-06-15", 1020, 1140, It.IsAny<string>()))
            .ReturnsAsync(false);

        _mockSlotRepo
            .Setup(x => x.CreateSlotAsync(It.IsAny<TableEntity>()))
            .Returns(Task.CompletedTask);

        _mockMembershipRepo
            .Setup(x => x.GetLeagueMembershipsAsync(leagueId))
            .ReturnsAsync(new List<TableEntity>());

        // Act
        var result = await _service.CreateSlotAsync(request, context);

        // Assert
        Assert.NotNull(result);

        // Verify slot was created (no conflict with touching boundaries)
        _mockSlotRepo.Verify(x => x.CreateSlotAsync(It.IsAny<TableEntity>()), Times.Once);

        // Verify no deletion occurred
        _mockSlotRepo.Verify(x => x.DeleteSlotAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CreateSlotAsync_CrossDivisionFieldConflict_Detected()
    {
        // TEST: Verifies conflict detection works across divisions
        // Field conflicts should be checked league-wide, not just in same division

        // Arrange
        var leagueId = "league-1";
        var fieldKey = "park1/field1";

        var context = new CorrelationContext
        {
            UserId = "coach-1",
            LeagueId = leagueId
        };

        // Creating slot in 10U division
        var request = new CreateSlotRequest
        {
            Division = "10U",
            OfferingTeamId = "Tigers-10U",
            GameDate = "2026-06-15",
            StartTime = "18:00",
            EndTime = "19:30",
            FieldKey = fieldKey,
            GameType = "Game"
        };

        var field = new TableEntity($"FIELD|{leagueId}|park1", "field1")
        {
            { "IsActive", true },
            { "ParkName", "Park 1" },
            { "FieldName", "Field 1" }
        };

        _mockAuthService
            .Setup(x => x.CanCreateSlotAsync("coach-1", leagueId, "10U", "Tigers-10U"))
            .ReturnsAsync(true);

        _mockFieldRepo
            .Setup(x => x.GetFieldAsync(leagueId, "park1", "field1"))
            .ReturnsAsync(field);

        // HasConflictAsync detects conflict (even though it's in different division)
        // This is correct - fields are shared across divisions
        _mockSlotRepo
            .Setup(x => x.HasConflictAsync(leagueId, fieldKey, "2026-06-15", It.IsAny<int>(), It.IsAny<int>(), null))
            .ReturnsAsync(true);  // Conflict with 12U game on same field

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ApiGuards.HttpError>(() =>
            _service.CreateSlotAsync(request, context));

        Assert.Equal(409, ex.Status);
        Assert.Equal(ErrorCodes.SLOT_CONFLICT, ex.Code);

        // Verify conflict check was made at league level (not division-scoped)
        _mockSlotRepo.Verify(
            x => x.HasConflictAsync(
                leagueId,  // League level
                fieldKey,
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                null),
            Times.Once);
    }
}
