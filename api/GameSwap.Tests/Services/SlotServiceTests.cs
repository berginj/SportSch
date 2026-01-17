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
        _mockSlotRepo
            .Setup(x => x.CreateSlotAsync(It.IsAny<TableEntity>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.CreateSlotAsync(request, context);

        // Assert
        Assert.NotNull(result);
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

    public void Dispose()
    {
        // Cleanup if needed
        GC.SuppressFinalize(this);
    }
}
