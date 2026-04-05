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

public class SlotServiceTests : IDisposable
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

    public SlotServiceTests()
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
            _mockLogger.Object
        );
    }

    [Fact]
    public async Task CreateSlotAsync_WithValidData_CreatesSlot()
    {
        // Arrange
        var request = new CreateSlotRequest
        {
            Division = "10U",
            OfferingTeamId = "team-1",
            GameDate = "2026-06-15",
            StartTime = "10:00",
            EndTime = "12:00",
            FieldKey = "park1/field1",
            ParkCode = "park1",
            FieldCode = "field1",
            ParkName = "Central Park",
            FieldName = "Field 1",
            IsExternalOffer = false,
            IsAvailability = false
        };

        var context = new CorrelationContext
        {
            UserId = "user-1",
            LeagueId = "league-1",
            CorrelationId = Guid.NewGuid().ToString()
        };

        // Mock successful authorization
        _mockAuthService
            .Setup(x => x.CanCreateSlotAsync(context.UserId, context.LeagueId, request.Division, request.OfferingTeamId))
            .ReturnsAsync(true);

        // Mock field exists
        var mockField = new TableEntity("partition", "rowkey")
        {
            { "ParkName", "Central Park" },
            { "FieldName", "Field 1" },
            { "IsActive", true }
        };
        _mockFieldRepo
            .Setup(x => x.GetFieldAsync(context.LeagueId, "park1", "field1"))
            .ReturnsAsync(mockField);

        // Mock no conflict
        _mockSlotRepo
            .Setup(x => x.HasConflictAsync(context.LeagueId, "park1/field1", request.GameDate, 600, 720, null))
            .ReturnsAsync(false);

        // Mock slot creation
        TableEntity? createdSlot = null;
        _mockSlotRepo
            .Setup(x => x.CreateSlotAsync(It.IsAny<TableEntity>()))
            .Callback<TableEntity>(entity => createdSlot = entity)
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.CreateSlotAsync(request, context);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(createdSlot);
        Assert.Equal(600, createdSlot!.GetInt32("StartMin"));
        Assert.Equal(720, createdSlot.GetInt32("EndMin"));
        _mockSlotRepo.Verify(x => x.CreateSlotAsync(It.IsAny<TableEntity>()), Times.Once);
    }

    [Fact]
    public async Task CreateSlotAsync_WithConflict_ThrowsConflictError()
    {
        // Arrange
        var request = new CreateSlotRequest
        {
            Division = "10U",
            OfferingTeamId = "team-1",
            GameDate = "2026-06-15",
            StartTime = "10:00",
            EndTime = "12:00",
            FieldKey = "park1/field1",
            ParkCode = "park1",
            FieldCode = "field1"
        };

        var context = new CorrelationContext
        {
            UserId = "user-1",
            LeagueId = "league-1",
            CorrelationId = Guid.NewGuid().ToString()
        };

        // Mock successful authorization
        _mockAuthService
            .Setup(x => x.CanCreateSlotAsync(context.UserId, context.LeagueId, request.Division, request.OfferingTeamId))
            .ReturnsAsync(true);

        // Mock field exists
        var mockField = new TableEntity("partition", "rowkey")
        {
            { "IsActive", true }
        };
        _mockFieldRepo
            .Setup(x => x.GetFieldAsync(context.LeagueId, "park1", "field1"))
            .ReturnsAsync(mockField);

        // Mock conflict exists
        _mockSlotRepo
            .Setup(x => x.HasConflictAsync(context.LeagueId, "park1/field1", request.GameDate, 600, 720, null))
            .ReturnsAsync(true);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ApiGuards.HttpError>(() =>
            _service.CreateSlotAsync(request, context)
        );

        Assert.Equal(409, ex.Status);
        Assert.Equal(ErrorCodes.SLOT_CONFLICT, ex.Code);

        // Verify slot was NOT created
        _mockSlotRepo.Verify(x => x.CreateSlotAsync(It.IsAny<TableEntity>()), Times.Never);
    }

    [Fact]
    public async Task CreateSlotAsync_WithNonExistentField_ThrowsNotFoundError()
    {
        // Arrange
        var request = new CreateSlotRequest
        {
            Division = "10U",
            OfferingTeamId = "team-1",
            GameDate = "2026-06-15",
            StartTime = "10:00",
            EndTime = "12:00",
            FieldKey = "park1/field1",
            ParkCode = "park1",
            FieldCode = "nonexistent"
        };

        var context = new CorrelationContext
        {
            UserId = "user-1",
            LeagueId = "league-1",
            CorrelationId = Guid.NewGuid().ToString()
        };

        // Mock successful authorization
        _mockAuthService
            .Setup(x => x.CanCreateSlotAsync(context.UserId, context.LeagueId, request.Division, request.OfferingTeamId))
            .ReturnsAsync(true);

        // Mock field does NOT exist
        _mockFieldRepo
            .Setup(x => x.GetFieldAsync(context.LeagueId, "park1", "nonexistent"))
            .ReturnsAsync((TableEntity?)null);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ApiGuards.HttpError>(() =>
            _service.CreateSlotAsync(request, context)
        );

        Assert.Equal(404, ex.Status);
        Assert.Equal(ErrorCodes.FIELD_NOT_FOUND, ex.Code);

        // Verify slot was NOT created
        _mockSlotRepo.Verify(x => x.CreateSlotAsync(It.IsAny<TableEntity>()), Times.Never);
    }

    [Fact]
    public async Task CreateSlotAsync_WithUnauthorizedUser_ThrowsForbiddenError()
    {
        // Arrange
        var request = new CreateSlotRequest
        {
            Division = "10U",
            OfferingTeamId = "team-1",
            GameDate = "2026-06-15",
            StartTime = "10:00",
            EndTime = "12:00",
            FieldKey = "park1/field1",
            ParkCode = "park1",
            FieldCode = "field1"
        };

        var context = new CorrelationContext
        {
            UserId = "unauthorized-user",
            LeagueId = "league-1",
            CorrelationId = Guid.NewGuid().ToString()
        };

        // Mock authorization failure
        _mockAuthService
            .Setup(x => x.CanCreateSlotAsync(context.UserId, context.LeagueId, request.Division, request.OfferingTeamId))
            .ReturnsAsync(false);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ApiGuards.HttpError>(() =>
            _service.CreateSlotAsync(request, context)
        );

        Assert.Equal(403, ex.Status);
        Assert.Equal(ErrorCodes.UNAUTHORIZED, ex.Code);

        // Verify slot was NOT created
        _mockSlotRepo.Verify(x => x.CreateSlotAsync(It.IsAny<TableEntity>()), Times.Never);
    }

    [Fact]
    public async Task CancelSlotAsync_WithValidSlot_UpdatesStatusToCancelled()
    {
        // Arrange
        var leagueId = "league-1";
        var division = "10U";
        var slotId = "slot-123";
        var userId = "user-1";

        var existingSlot = new TableEntity("pk", slotId)
        {
            { "Status", Constants.Status.SlotOpen },
            { "OfferingTeamId", "team-1" },
            { "ConfirmedTeamId", "" }
        };
        existingSlot.ETag = new ETag("etag-123");

        _mockSlotRepo
            .Setup(x => x.GetSlotAsync(leagueId, division, slotId))
            .ReturnsAsync(existingSlot);

        _mockAuthService
            .Setup(x => x.CanCancelSlotAsync(userId, leagueId, "team-1", ""))
            .ReturnsAsync(true);

        _mockSlotRepo
            .Setup(x => x.CancelSlotAsync(leagueId, division, slotId))
            .Returns(Task.CompletedTask);

        // Act
        await _service.CancelSlotAsync(leagueId, division, slotId, userId);

        // Assert
        _mockSlotRepo.Verify(x => x.CancelSlotAsync(leagueId, division, slotId), Times.Once);
    }

    [Fact]
    public async Task CancelSlotAsync_WithConfirmedGame_NotifiesBothTeamCoaches()
    {
        // Arrange
        var leagueId = "league-1";
        var division = "10U";
        var slotId = "slot-123";
        var userId = "admin-user";
        var notifications = new List<(string userId, string type, string message)>();
        var cancelledEmails = new List<string>();

        var existingSlot = new TableEntity("pk", slotId)
        {
            { "Status", Constants.Status.SlotConfirmed },
            { "OfferingTeamId", "team-1" },
            { "ConfirmedTeamId", "team-2" },
            { "GameDate", "2026-06-15" },
            { "StartTime", "18:00" },
            { "DisplayName", "Diamond 1" }
        };
        existingSlot.ETag = new ETag("etag-123");

        _mockSlotRepo
            .Setup(x => x.GetSlotAsync(leagueId, division, slotId))
            .ReturnsAsync(existingSlot);

        _mockAuthService
            .Setup(x => x.CanCancelSlotAsync(userId, leagueId, "team-1", "team-2"))
            .ReturnsAsync(true);

        _mockSlotRepo
            .Setup(x => x.CancelSlotAsync(leagueId, division, slotId))
            .Returns(Task.CompletedTask);

        _mockMembershipRepo
            .Setup(x => x.GetLeagueMembershipsAsync(leagueId))
            .ReturnsAsync(new List<TableEntity>
            {
                new("offer-coach", leagueId)
                {
                    { "Role", Constants.Roles.Coach },
                    { "Division", division },
                    { "TeamId", "team-1" },
                    { "Email", "offer@example.com" }
                },
                new("confirm-coach", leagueId)
                {
                    { "Role", Constants.Roles.Coach },
                    { "Division", division },
                    { "TeamId", "team-2" },
                    { "Email", "confirm@example.com" }
                }
            });

        _mockNotificationService
            .Setup(x => x.CreateNotificationAsync(
                It.IsAny<string>(),
                leagueId,
                "SlotCancelled",
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
            .Setup(x => x.ShouldSendEmailAsync("offer-coach", leagueId, "SlotCancelled"))
            .ReturnsAsync(true);
        _mockPreferencesService
            .Setup(x => x.ShouldSendEmailAsync("confirm-coach", leagueId, "SlotCancelled"))
            .ReturnsAsync(true);

        _mockEmailService
            .Setup(x => x.SendGameCancelledEmailAsync(
                It.IsAny<string>(),
                leagueId,
                "2026-06-15",
                "18:00",
                "Diamond 1",
                It.IsAny<string>()))
            .Callback<string, string, string, string, string, string>((to, _, _, _, _, _) =>
            {
                cancelledEmails.Add(to);
            })
            .Returns(Task.CompletedTask);

        // Act
        await _service.CancelSlotAsync(leagueId, division, slotId, userId);
        await WaitForConditionAsync(() => notifications.Count == 2 && cancelledEmails.Count == 2);

        // Assert
        Assert.Collection(
            notifications.OrderBy(x => x.userId),
            item =>
            {
                Assert.Equal("confirm-coach", item.userId);
                Assert.Equal("SlotCancelled", item.type);
                Assert.Contains("Game cancelled", item.message);
            },
            item =>
            {
                Assert.Equal("offer-coach", item.userId);
                Assert.Equal("SlotCancelled", item.type);
                Assert.Contains("Game cancelled", item.message);
            });

        Assert.Equal(2, cancelledEmails.Count);
        Assert.Contains("offer@example.com", cancelledEmails);
        Assert.Contains("confirm@example.com", cancelledEmails);
    }

    [Fact]
    public async Task CancelSlotAsync_WithNonExistentSlot_ThrowsNotFoundError()
    {
        // Arrange
        var leagueId = "league-1";
        var division = "10U";
        var slotId = "nonexistent-slot";
        var userId = "user-1";

        _mockSlotRepo
            .Setup(x => x.GetSlotAsync(leagueId, division, slotId))
            .ReturnsAsync((TableEntity?)null);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ApiGuards.HttpError>(() =>
            _service.CancelSlotAsync(leagueId, division, slotId, userId)
        );

        Assert.Equal(404, ex.Status);
        Assert.Equal(ErrorCodes.SLOT_NOT_FOUND, ex.Code);

        // Verify cancel was NOT called
        _mockSlotRepo.Verify(x => x.CancelSlotAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task QuerySlotsAsync_WithMultipleStatuses_PassesCanonicalFilterToRepository()
    {
        SlotQueryFilter? capturedFilter = null;
        var entity = new TableEntity("SLOT|league-1|AAA", "slot-1")
        {
            { "LeagueId", "league-1" },
            { "Division", "AAA" },
            { "Status", Constants.Status.SlotConfirmed },
            { "GameDate", "2026-03-20" },
            { "StartTime", "18:00" },
            { "DisplayName", "Field 1" }
        };

        _mockSlotRepo
            .Setup(x => x.QuerySlotsAsync(It.IsAny<SlotQueryFilter>(), null))
            .Callback<SlotQueryFilter, string?>((filter, _) => capturedFilter = filter)
            .ReturnsAsync(new PaginationResult<TableEntity>
            {
                Items = new List<TableEntity> { entity },
                PageSize = 50,
                ContinuationToken = null
            });

        var request = new SlotQueryRequest
        {
            LeagueId = "league-1",
            Division = "AAA",
            Status = $"{Constants.Status.SlotOpen},{Constants.Status.SlotConfirmed}",
            FromDate = "2026-03-14",
            ToDate = "2026-06-10",
            PageSize = 50
        };

        var context = new CorrelationContext
        {
            LeagueId = "league-1",
            UserId = "user-1",
            CorrelationId = Guid.NewGuid().ToString()
        };

        // Act
        var result = await _service.QuerySlotsAsync(request, context);

        // Assert
        Assert.NotNull(capturedFilter);
        Assert.Equal(new[] { Constants.Status.SlotOpen, Constants.Status.SlotConfirmed }, capturedFilter!.Statuses);
        Assert.False(capturedFilter.ExcludeCancelled);
        Assert.Equal("2026-03-14", capturedFilter.FromDate);
        Assert.Equal("2026-06-10", capturedFilter.ToDate);
        Assert.Single(result.Items);
        Assert.Null(result.ContinuationToken);
        Assert.Equal(50, result.PageSize);
    }

    [Fact]
    public async Task QuerySlotsAsync_WithExcludeAvailability_PassesCanonicalFilterToRepository()
    {
        SlotQueryFilter? capturedFilter = null;

        var scheduledGame = new TableEntity("SLOT|league-1|AAA", "slot-b")
        {
            { "LeagueId", "league-1" },
            { "Division", "AAA" },
            { "Status", Constants.Status.SlotConfirmed },
            { "IsAvailability", false },
            { "GameDate", "2026-03-22" },
            { "StartTime", "18:00" },
            { "DisplayName", "Field B" }
        };

        _mockSlotRepo
            .Setup(x => x.QuerySlotsAsync(It.IsAny<SlotQueryFilter>(), null))
            .Callback<SlotQueryFilter, string?>((filter, _) => capturedFilter = filter)
            .ReturnsAsync(new PaginationResult<TableEntity>
            {
                Items = new List<TableEntity> { scheduledGame },
                PageSize = 50,
                ContinuationToken = null
            });

        var request = new SlotQueryRequest
        {
            LeagueId = "league-1",
            Division = "AAA",
            Status = $"{Constants.Status.SlotOpen},{Constants.Status.SlotConfirmed}",
            ExcludeAvailability = true,
            FromDate = "2026-03-14",
            ToDate = "2026-06-10",
            PageSize = 50
        };

        var context = new CorrelationContext
        {
            LeagueId = "league-1",
            UserId = "user-1",
            CorrelationId = Guid.NewGuid().ToString()
        };

        // Act
        var result = await _service.QuerySlotsAsync(request, context);

        // Assert
        Assert.NotNull(capturedFilter);
        Assert.True(capturedFilter!.ExcludeAvailability);
        Assert.Equal(new[] { Constants.Status.SlotOpen, Constants.Status.SlotConfirmed }, capturedFilter.Statuses);
        Assert.Single(result.Items);
        Assert.Null(result.ContinuationToken);
        Assert.Equal(50, result.PageSize);
    }

    public void Dispose()
    {
        // Cleanup if needed
        GC.SuppressFinalize(this);
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
