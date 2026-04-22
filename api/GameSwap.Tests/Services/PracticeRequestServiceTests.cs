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

public class PracticeRequestServiceTests
{
    private readonly Mock<IPracticeRequestRepository> _mockPracticeRequestRepo;
    private readonly Mock<IMembershipRepository> _mockMembershipRepo;
    private readonly Mock<ISlotRepository> _mockSlotRepo;
    private readonly Mock<ITeamRepository> _mockTeamRepo;
    private readonly Mock<ILogger<PracticeRequestService>> _mockLogger;
    private readonly PracticeRequestService _service;

    public PracticeRequestServiceTests()
    {
        _mockPracticeRequestRepo = new Mock<IPracticeRequestRepository>();
        _mockMembershipRepo = new Mock<IMembershipRepository>();
        _mockSlotRepo = new Mock<ISlotRepository>();
        _mockTeamRepo = new Mock<ITeamRepository>();
        _mockLogger = new Mock<ILogger<PracticeRequestService>>();

        _service = new PracticeRequestService(
            _mockPracticeRequestRepo.Object,
            _mockMembershipRepo.Object,
            _mockSlotRepo.Object,
            _mockTeamRepo.Object,
            _mockLogger.Object);
    }

    [Fact]
    public async Task CreateRequestAsync_CoachForDifferentTeam_ThrowsForbidden()
    {
        // Arrange
        var membership = BuildMembership(Constants.Roles.Coach, "10U", "Panthers");
        _mockMembershipRepo
            .Setup(x => x.GetMembershipAsync("coach-1", "league-1"))
            .ReturnsAsync(membership);
        _mockMembershipRepo
            .Setup(x => x.IsGlobalAdminAsync("coach-1"))
            .ReturnsAsync(false);

        // Act + Assert
        var ex = await Assert.ThrowsAsync<ApiGuards.HttpError>(() =>
            _service.CreateRequestAsync("league-1", "coach-1", "10U", "Tigers", "slot-1", "reason", false, null));

        Assert.Equal(403, ex.Status);
        Assert.Equal(ErrorCodes.FORBIDDEN, ex.Code);
        _mockPracticeRequestRepo.Verify(x => x.CreateRequestAsync(It.IsAny<TableEntity>()), Times.Never);
    }

    [Fact]
    public async Task CreateRequestAsync_OpenAvailabilitySlot_CreatesPendingRequestAndReservesSlot()
    {
        // Arrange
        var membership = BuildMembership(Constants.Roles.Coach, "10U", "Panthers");
        var slot = new TableEntity("SLOT|league-1|10U", "slot-1")
        {
            ["Status"] = Constants.Status.SlotOpen,
            ["IsAvailability"] = true,
            ["AllocationSlotType"] = "Practice"
        };
        slot.ETag = new ETag("slot-etag-1");

        _mockMembershipRepo
            .Setup(x => x.GetMembershipAsync("coach-1", "league-1"))
            .ReturnsAsync(membership);
        _mockMembershipRepo
            .Setup(x => x.IsGlobalAdminAsync("coach-1"))
            .ReturnsAsync(false);
        _mockTeamRepo
            .Setup(x => x.GetTeamAsync("league-1", "10U", "Panthers"))
            .ReturnsAsync(new TableEntity("TEAM|league-1|10U", "Panthers"));
        _mockSlotRepo
            .Setup(x => x.GetSlotAsync("league-1", "10U", "slot-1"))
            .ReturnsAsync(slot);
        _mockPracticeRequestRepo
            .Setup(x => x.CountRequestsForTeamAsync("league-1", "10U", "Panthers", It.IsAny<IReadOnlyCollection<string>>()))
            .ReturnsAsync(0);
        _mockPracticeRequestRepo
            .Setup(x => x.ExistsRequestForTeamSlotAsync("league-1", "10U", "Panthers", "slot-1", It.IsAny<IReadOnlyCollection<string>>()))
            .ReturnsAsync(false);
        _mockPracticeRequestRepo
            .Setup(x => x.QuerySlotRequestsAsync("league-1", "10U", "slot-1", It.IsAny<IReadOnlyCollection<string>>()))
            .ReturnsAsync(new List<TableEntity>());

        TableEntity? updatedSlot = null;
        _mockSlotRepo
            .Setup(x => x.UpdateSlotAsync(It.IsAny<TableEntity>(), It.IsAny<ETag>()))
            .Callback<TableEntity, ETag>((entity, _) => updatedSlot = entity)
            .Returns(Task.CompletedTask);

        _mockPracticeRequestRepo
            .Setup(x => x.CreateRequestAsync(It.IsAny<TableEntity>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.CreateRequestAsync(
            leagueId: "league-1",
            userId: "coach-1",
            division: "10U",
            teamId: "Panthers",
            slotId: "slot-1",
            reason: "Practice request",
            openToShareField: false,
            shareWithTeamId: null);

        // Assert
        Assert.Equal("Pending", result.GetString("Status"));
        Assert.Equal("Panthers", result.GetString("TeamId"));
        Assert.Equal("slot-1", result.GetString("SlotId"));
        Assert.NotNull(updatedSlot);
        Assert.Equal("Pending", updatedSlot!.GetString("Status"));
        Assert.Equal(result.RowKey, updatedSlot.GetString("PendingRequestId"));
        Assert.Equal("Panthers", updatedSlot.GetString("PendingTeamId"));
        _mockPracticeRequestRepo.Verify(x => x.CreateRequestAsync(It.IsAny<TableEntity>()), Times.Once);
        _mockSlotRepo.Verify(x => x.UpdateSlotAsync(It.IsAny<TableEntity>(), It.IsAny<ETag>()), Times.Once);
    }

    [Fact]
    public async Task ApproveRequestAsync_PendingRequest_ConfirmsSlotAndRejectsCompetingPendingRequests()
    {
        // Arrange
        var membership = BuildMembership(Constants.Roles.LeagueAdmin, "", "");
        var requestEntity = new TableEntity("PRACTICEREQ|league-1", "req-1")
        {
            ["Status"] = "Pending",
            ["Division"] = "10U",
            ["TeamId"] = "Panthers",
            ["SlotId"] = "slot-1"
        };
        requestEntity.ETag = new ETag("req-etag-1");

        var slot = new TableEntity("SLOT|league-1|10U", "slot-1")
        {
            ["Status"] = "Pending",
            ["PendingRequestId"] = "req-1",
            ["PendingTeamId"] = "Panthers"
        };
        slot.ETag = new ETag("slot-etag-1");

        var competingRequest = new TableEntity("PRACTICEREQ|league-1", "req-2")
        {
            ["Status"] = "Pending",
            ["Division"] = "10U",
            ["TeamId"] = "Tigers",
            ["SlotId"] = "slot-1"
        };
        competingRequest.ETag = new ETag("req-etag-2");

        _mockMembershipRepo
            .Setup(x => x.IsGlobalAdminAsync("admin-1"))
            .ReturnsAsync(false);
        _mockMembershipRepo
            .Setup(x => x.GetMembershipAsync("admin-1", "league-1"))
            .ReturnsAsync(membership);
        _mockPracticeRequestRepo
            .Setup(x => x.GetRequestAsync("league-1", "req-1"))
            .ReturnsAsync(requestEntity);
        _mockPracticeRequestRepo
            .Setup(x => x.UpdateRequestAsync(It.IsAny<TableEntity>(), It.IsAny<ETag>()))
            .Returns(Task.CompletedTask);
        _mockSlotRepo
            .Setup(x => x.GetSlotAsync("league-1", "10U", "slot-1"))
            .ReturnsAsync(slot);
        _mockSlotRepo
            .Setup(x => x.UpdateSlotAsync(It.IsAny<TableEntity>(), It.IsAny<ETag>()))
            .Returns(Task.CompletedTask);
        _mockPracticeRequestRepo
            .Setup(x => x.QuerySlotRequestsAsync("league-1", "10U", "slot-1", It.IsAny<IReadOnlyCollection<string>>()))
            .ReturnsAsync(new List<TableEntity>
            {
                new("PRACTICEREQ|league-1", "req-1") { ["Status"] = "Pending" },
                competingRequest
            });

        // Act
        var result = await _service.ApproveRequestAsync("league-1", "admin-1", "req-1", "Approved by commissioner");

        // Assert
        Assert.Equal("Approved", result.GetString("Status"));
        Assert.Equal(Constants.Status.SlotConfirmed, slot.GetString("Status"));
        Assert.Equal("req-1", slot.GetString("ConfirmedRequestId"));
        Assert.Equal("Panthers", slot.GetString("ConfirmedTeamId"));
        Assert.Equal("Rejected", competingRequest.GetString("Status"));
        _mockPracticeRequestRepo.Verify(x => x.UpdateRequestAsync(It.IsAny<TableEntity>(), It.IsAny<ETag>()), Times.AtLeast(2));
        _mockSlotRepo.Verify(x => x.UpdateSlotAsync(It.IsAny<TableEntity>(), It.IsAny<ETag>()), Times.Once);
    }

    [Fact]
    public async Task RejectRequestAsync_PendingRequest_ReopensSlotWhenNoOtherPending()
    {
        // Arrange
        var membership = BuildMembership(Constants.Roles.LeagueAdmin, "", "");
        var requestEntity = new TableEntity("PRACTICEREQ|league-1", "req-1")
        {
            ["Status"] = "Pending",
            ["Division"] = "10U",
            ["TeamId"] = "Panthers",
            ["SlotId"] = "slot-1"
        };
        requestEntity.ETag = new ETag("req-etag-1");

        var slot = new TableEntity("SLOT|league-1|10U", "slot-1")
        {
            ["Status"] = "Pending",
            ["PendingRequestId"] = "req-1",
            ["PendingTeamId"] = "Panthers"
        };
        slot.ETag = new ETag("slot-etag-1");

        _mockMembershipRepo
            .Setup(x => x.IsGlobalAdminAsync("admin-1"))
            .ReturnsAsync(false);
        _mockMembershipRepo
            .Setup(x => x.GetMembershipAsync("admin-1", "league-1"))
            .ReturnsAsync(membership);
        _mockPracticeRequestRepo
            .Setup(x => x.GetRequestAsync("league-1", "req-1"))
            .ReturnsAsync(requestEntity);
        _mockPracticeRequestRepo
            .Setup(x => x.UpdateRequestAsync(It.IsAny<TableEntity>(), It.IsAny<ETag>()))
            .Returns(Task.CompletedTask);
        _mockPracticeRequestRepo
            .Setup(x => x.QuerySlotRequestsAsync("league-1", "10U", "slot-1", It.IsAny<IReadOnlyCollection<string>>()))
            .ReturnsAsync(new List<TableEntity>());
        _mockSlotRepo
            .Setup(x => x.GetSlotAsync("league-1", "10U", "slot-1"))
            .ReturnsAsync(slot);
        _mockSlotRepo
            .Setup(x => x.UpdateSlotAsync(It.IsAny<TableEntity>(), It.IsAny<ETag>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.RejectRequestAsync("league-1", "admin-1", "req-1", "No longer available");

        // Assert
        Assert.Equal("Rejected", result.GetString("Status"));
        Assert.Equal(Constants.Status.SlotOpen, slot.GetString("Status"));
        Assert.Equal("", slot.GetString("PendingRequestId"));
        Assert.Equal("", slot.GetString("PendingTeamId"));
        _mockSlotRepo.Verify(x => x.UpdateSlotAsync(It.IsAny<TableEntity>(), It.IsAny<ETag>()), Times.Once);
    }

    [Fact]
    public async Task ApproveRequestAsync_NonPendingRequest_ThrowsConflict()
    {
        // Arrange
        var membership = BuildMembership(Constants.Roles.LeagueAdmin, "", "");
        var requestEntity = new TableEntity("PRACTICEREQ|league-1", "req-1")
        {
            ["Status"] = "Approved",
            ["Division"] = "10U",
            ["TeamId"] = "Panthers",
            ["SlotId"] = "slot-1"
        };

        _mockMembershipRepo
            .Setup(x => x.IsGlobalAdminAsync("admin-1"))
            .ReturnsAsync(false);
        _mockMembershipRepo
            .Setup(x => x.GetMembershipAsync("admin-1", "league-1"))
            .ReturnsAsync(membership);
        _mockPracticeRequestRepo
            .Setup(x => x.GetRequestAsync("league-1", "req-1"))
            .ReturnsAsync(requestEntity);

        // Act + Assert
        var ex = await Assert.ThrowsAsync<ApiGuards.HttpError>(() =>
            _service.ApproveRequestAsync("league-1", "admin-1", "req-1", "already approved"));

        Assert.Equal(409, ex.Status);
        Assert.Equal(ErrorCodes.REQUEST_NOT_PENDING, ex.Code);
    }

    [Fact]
    public async Task CreateMoveRequestAsync_ApprovedRequest_CreatesNewMoveRequest()
    {
        // Arrange
        var membership = BuildMembership(Constants.Roles.Coach, "10U", "Panthers");
        var sourceRequest = new TableEntity("PRACTICEREQ|league-1", "req-1")
        {
            ["Status"] = "Approved",
            ["Division"] = "10U",
            ["TeamId"] = "Panthers",
            ["SlotId"] = "slot-1",
            ["Priority"] = 1
        };
        sourceRequest.ETag = new ETag("req-etag-1");

        var targetSlot = new TableEntity("SLOT|league-1|10U", "slot-2")
        {
            ["Status"] = Constants.Status.SlotOpen,
            ["IsAvailability"] = true,
            ["AllocationSlotType"] = "Practice",
            ["FieldKey"] = "park1/field2",
            ["DisplayName"] = "Field 2"
        };
        targetSlot.ETag = new ETag("slot-etag-2");

        _mockMembershipRepo
            .Setup(x => x.GetMembershipAsync("coach-1", "league-1"))
            .ReturnsAsync(membership);
        _mockMembershipRepo
            .Setup(x => x.IsGlobalAdminAsync("coach-1"))
            .ReturnsAsync(false);
        _mockPracticeRequestRepo
            .Setup(x => x.GetRequestAsync("league-1", "req-1"))
            .ReturnsAsync(sourceRequest);
        _mockTeamRepo
            .Setup(x => x.GetTeamAsync("league-1", "10U", "Panthers"))
            .ReturnsAsync(new TableEntity("TEAM|league-1|10U", "Panthers"));
        _mockSlotRepo
            .Setup(x => x.GetSlotAsync("league-1", "10U", "slot-2"))
            .ReturnsAsync(targetSlot);
        _mockPracticeRequestRepo
            .Setup(x => x.QueryRequestsAsync("league-1", null, "10U", "Panthers", null))
            .ReturnsAsync(new List<TableEntity> { sourceRequest });
        _mockPracticeRequestRepo
            .Setup(x => x.QuerySlotRequestsAsync("league-1", "10U", "slot-2", It.IsAny<IReadOnlyCollection<string>>()))
            .ReturnsAsync(new List<TableEntity>());
        _mockSlotRepo
            .Setup(x => x.UpdateSlotAsync(It.IsAny<TableEntity>(), It.IsAny<ETag>()))
            .Returns(Task.CompletedTask);
        _mockPracticeRequestRepo
            .Setup(x => x.CreateRequestAsync(It.IsAny<TableEntity>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.CreateMoveRequestAsync(
            leagueId: "league-1",
            userId: "coach-1",
            sourceRequestId: "req-1",
            targetSlotId: "slot-2",
            reason: "Moving to better time",
            openToShareField: false,
            shareWithTeamId: null);

        // Assert
        Assert.Equal("Pending", result.GetString("Status"));
        Assert.Equal("Panthers", result.GetString("TeamId"));
        Assert.Equal("slot-2", result.GetString("SlotId"));
        Assert.Equal("Move", result.GetString("RequestKind"));
        Assert.Equal("req-1", result.GetString("MoveFromRequestId"));
        Assert.Equal("slot-1", result.GetString("MoveFromSlotId"));
        Assert.Equal("Approved", result.GetString("MoveFromStatus"));
        _mockPracticeRequestRepo.Verify(x => x.CreateRequestAsync(It.IsAny<TableEntity>()), Times.Once);
    }

    [Fact]
    public async Task CreateMoveRequestAsync_PendingRequest_CreatesNewMoveRequest()
    {
        // Arrange
        var membership = BuildMembership(Constants.Roles.Coach, "10U", "Panthers");
        var sourceRequest = new TableEntity("PRACTICEREQ|league-1", "req-1")
        {
            ["Status"] = "Pending",
            ["Division"] = "10U",
            ["TeamId"] = "Panthers",
            ["SlotId"] = "slot-1",
            ["Priority"] = 1
        };
        sourceRequest.ETag = new ETag("req-etag-1");

        var targetSlot = new TableEntity("SLOT|league-1|10U", "slot-2")
        {
            ["Status"] = Constants.Status.SlotOpen,
            ["IsAvailability"] = true,
            ["AllocationSlotType"] = "Practice"
        };
        targetSlot.ETag = new ETag("slot-etag-2");

        _mockMembershipRepo
            .Setup(x => x.GetMembershipAsync("coach-1", "league-1"))
            .ReturnsAsync(membership);
        _mockMembershipRepo
            .Setup(x => x.IsGlobalAdminAsync("coach-1"))
            .ReturnsAsync(false);
        _mockPracticeRequestRepo
            .Setup(x => x.GetRequestAsync("league-1", "req-1"))
            .ReturnsAsync(sourceRequest);
        _mockTeamRepo
            .Setup(x => x.GetTeamAsync("league-1", "10U", "Panthers"))
            .ReturnsAsync(new TableEntity("TEAM|league-1|10U", "Panthers"));
        _mockSlotRepo
            .Setup(x => x.GetSlotAsync("league-1", "10U", "slot-2"))
            .ReturnsAsync(targetSlot);
        _mockPracticeRequestRepo
            .Setup(x => x.QueryRequestsAsync("league-1", null, "10U", "Panthers", null))
            .ReturnsAsync(new List<TableEntity> { sourceRequest });
        _mockPracticeRequestRepo
            .Setup(x => x.QuerySlotRequestsAsync("league-1", "10U", "slot-2", It.IsAny<IReadOnlyCollection<string>>()))
            .ReturnsAsync(new List<TableEntity>());
        _mockSlotRepo
            .Setup(x => x.UpdateSlotAsync(It.IsAny<TableEntity>(), It.IsAny<ETag>()))
            .Returns(Task.CompletedTask);
        _mockPracticeRequestRepo
            .Setup(x => x.CreateRequestAsync(It.IsAny<TableEntity>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.CreateMoveRequestAsync(
            leagueId: "league-1",
            userId: "coach-1",
            sourceRequestId: "req-1",
            targetSlotId: "slot-2",
            reason: "Moving to better time",
            openToShareField: false,
            shareWithTeamId: null);

        // Assert
        Assert.Equal("Pending", result.GetString("Status"));
        Assert.Equal("Move", result.GetString("RequestKind"));
        Assert.Equal("req-1", result.GetString("MoveFromRequestId"));
        _mockPracticeRequestRepo.Verify(x => x.CreateRequestAsync(It.IsAny<TableEntity>()), Times.Once);
    }

    [Fact]
    public async Task CreateMoveRequestAsync_CancelledRequest_ThrowsConflict()
    {
        // Arrange
        var membership = BuildMembership(Constants.Roles.Coach, "10U", "Panthers");
        var sourceRequest = new TableEntity("PRACTICEREQ|league-1", "req-1")
        {
            ["Status"] = "Cancelled",
            ["Division"] = "10U",
            ["TeamId"] = "Panthers",
            ["SlotId"] = "slot-1"
        };

        _mockMembershipRepo
            .Setup(x => x.GetMembershipAsync("coach-1", "league-1"))
            .ReturnsAsync(membership);
        _mockMembershipRepo
            .Setup(x => x.IsGlobalAdminAsync("coach-1"))
            .ReturnsAsync(false);
        _mockPracticeRequestRepo
            .Setup(x => x.GetRequestAsync("league-1", "req-1"))
            .ReturnsAsync(sourceRequest);

        // Act + Assert
        var ex = await Assert.ThrowsAsync<ApiGuards.HttpError>(() =>
            _service.CreateMoveRequestAsync("league-1", "coach-1", "req-1", "slot-2", "reason", false, null));

        Assert.Equal(409, ex.Status);
        Assert.Equal(ErrorCodes.PRACTICE_MOVE_NOT_ALLOWED, ex.Code);
        _mockPracticeRequestRepo.Verify(x => x.CreateRequestAsync(It.IsAny<TableEntity>()), Times.Never);
    }

    [Fact]
    public async Task CreateMoveRequestAsync_MoveToSameSlot_ThrowsConflict()
    {
        // Arrange
        var membership = BuildMembership(Constants.Roles.Coach, "10U", "Panthers");
        var sourceRequest = new TableEntity("PRACTICEREQ|league-1", "req-1")
        {
            ["Status"] = "Approved",
            ["Division"] = "10U",
            ["TeamId"] = "Panthers",
            ["SlotId"] = "slot-1"
        };

        _mockMembershipRepo
            .Setup(x => x.GetMembershipAsync("coach-1", "league-1"))
            .ReturnsAsync(membership);
        _mockMembershipRepo
            .Setup(x => x.IsGlobalAdminAsync("coach-1"))
            .ReturnsAsync(false);
        _mockPracticeRequestRepo
            .Setup(x => x.GetRequestAsync("league-1", "req-1"))
            .ReturnsAsync(sourceRequest);

        // Act + Assert
        var ex = await Assert.ThrowsAsync<ApiGuards.HttpError>(() =>
            _service.CreateMoveRequestAsync("league-1", "coach-1", "req-1", "slot-1", "reason", false, null));

        Assert.Equal(409, ex.Status);
        Assert.Equal(ErrorCodes.PRACTICE_MOVE_NOT_ALLOWED, ex.Code);
        Assert.Contains("different practice slot", ex.Message);
        _mockPracticeRequestRepo.Verify(x => x.CreateRequestAsync(It.IsAny<TableEntity>()), Times.Never);
    }

    [Fact]
    public async Task CreateMoveRequestAsync_CoachMovingOtherTeamRequest_ThrowsForbidden()
    {
        // Arrange
        var membership = BuildMembership(Constants.Roles.Coach, "10U", "Panthers");
        var sourceRequest = new TableEntity("PRACTICEREQ|league-1", "req-1")
        {
            ["Status"] = "Approved",
            ["Division"] = "10U",
            ["TeamId"] = "Tigers",
            ["SlotId"] = "slot-1"
        };

        _mockMembershipRepo
            .Setup(x => x.GetMembershipAsync("coach-1", "league-1"))
            .ReturnsAsync(membership);
        _mockMembershipRepo
            .Setup(x => x.IsGlobalAdminAsync("coach-1"))
            .ReturnsAsync(false);
        _mockPracticeRequestRepo
            .Setup(x => x.GetRequestAsync("league-1", "req-1"))
            .ReturnsAsync(sourceRequest);

        // Act + Assert
        var ex = await Assert.ThrowsAsync<ApiGuards.HttpError>(() =>
            _service.CreateMoveRequestAsync("league-1", "coach-1", "req-1", "slot-2", "reason", false, null));

        Assert.Equal(403, ex.Status);
        Assert.Equal(ErrorCodes.FORBIDDEN, ex.Code);
        _mockPracticeRequestRepo.Verify(x => x.CreateRequestAsync(It.IsAny<TableEntity>()), Times.Never);
    }

    [Fact]
    public async Task CreateMoveRequestAsync_NonExistentSourceRequest_ThrowsNotFound()
    {
        // Arrange
        _mockPracticeRequestRepo
            .Setup(x => x.GetRequestAsync("league-1", "req-1"))
            .ReturnsAsync((TableEntity?)null);

        // Act + Assert
        var ex = await Assert.ThrowsAsync<ApiGuards.HttpError>(() =>
            _service.CreateMoveRequestAsync("league-1", "coach-1", "req-1", "slot-2", "reason", false, null));

        Assert.Equal(404, ex.Status);
        Assert.Equal(ErrorCodes.REQUEST_NOT_FOUND, ex.Code);
    }

    [Fact]
    public async Task CreateMoveRequestAsync_WithinLeadTime_ThrowsConflict()
    {
        // Arrange
        var membership = BuildMembership(Constants.Roles.Coach, "10U", "Panthers");

        // Practice happening in 24 hours (within 48-hour lead time)
        var tomorrow = DateTime.UtcNow.AddHours(24);
        var practiceDate = tomorrow.ToString("yyyy-MM-dd");
        var practiceTime = tomorrow.ToString("HH:mm");

        var sourceRequest = new TableEntity("PRACTICEREQ|league-1", "req-1")
        {
            ["Status"] = "Approved",
            ["Division"] = "10U",
            ["TeamId"] = "Panthers",
            ["SlotId"] = "slot-1",
            ["Priority"] = 1
        };
        sourceRequest.ETag = new ETag("req-etag-1");

        var sourceSlot = new TableEntity("SLOT|league-1|10U", "slot-1")
        {
            ["GameDate"] = practiceDate,
            ["StartTime"] = practiceTime,
            ["EndTime"] = tomorrow.AddMinutes(90).ToString("HH:mm"),
        };

        _mockMembershipRepo
            .Setup(x => x.GetMembershipAsync("coach-1", "league-1"))
            .ReturnsAsync(membership);
        _mockMembershipRepo
            .Setup(x => x.IsGlobalAdminAsync("coach-1"))
            .ReturnsAsync(false);
        _mockPracticeRequestRepo
            .Setup(x => x.GetRequestAsync("league-1", "req-1"))
            .ReturnsAsync(sourceRequest);
        _mockSlotRepo
            .Setup(x => x.GetSlotAsync("league-1", "10U", "slot-1"))
            .ReturnsAsync(sourceSlot);

        // Act + Assert
        var ex = await Assert.ThrowsAsync<ApiGuards.HttpError>(() =>
            _service.CreateMoveRequestAsync("league-1", "coach-1", "req-1", "slot-2", "reason", false, null));

        Assert.Equal(409, ex.Status);
        Assert.Equal(ErrorCodes.LEAD_TIME_VIOLATION, ex.Code);
        Assert.Contains("72 hours", ex.Message);
        Assert.Contains("24", ex.Message); // Hours until practice
        _mockPracticeRequestRepo.Verify(x => x.CreateRequestAsync(It.IsAny<TableEntity>()), Times.Never);
    }

    [Fact]
    public async Task CreateMoveRequestAsync_OutsideLeadTime_AllowsMove()
    {
        // Arrange
        var membership = BuildMembership(Constants.Roles.Coach, "10U", "Panthers");

        // Practice happening in 96 hours (outside 72-hour lead time)
        var futureDate = DateTime.UtcNow.AddHours(96);
        var practiceDate = futureDate.ToString("yyyy-MM-dd");
        var practiceTime = futureDate.ToString("HH:mm");

        var sourceRequest = new TableEntity("PRACTICEREQ|league-1", "req-1")
        {
            ["Status"] = "Approved",
            ["Division"] = "10U",
            ["TeamId"] = "Panthers",
            ["SlotId"] = "slot-1",
            ["Priority"] = 1
        };
        sourceRequest.ETag = new ETag("req-etag-1");

        var sourceSlot = new TableEntity("SLOT|league-1|10U", "slot-1")
        {
            ["GameDate"] = practiceDate,
            ["StartTime"] = practiceTime,
            ["EndTime"] = futureDate.AddMinutes(90).ToString("HH:mm"),
        };

        var targetSlot = new TableEntity("SLOT|league-1|10U", "slot-2")
        {
            ["Status"] = Constants.Status.SlotOpen,
            ["IsAvailability"] = true,
            ["AllocationSlotType"] = "Practice"
        };
        targetSlot.ETag = new ETag("slot-etag-2");

        _mockMembershipRepo
            .Setup(x => x.GetMembershipAsync("coach-1", "league-1"))
            .ReturnsAsync(membership);
        _mockMembershipRepo
            .Setup(x => x.IsGlobalAdminAsync("coach-1"))
            .ReturnsAsync(false);
        _mockPracticeRequestRepo
            .Setup(x => x.GetRequestAsync("league-1", "req-1"))
            .ReturnsAsync(sourceRequest);
        _mockSlotRepo
            .Setup(x => x.GetSlotAsync("league-1", "10U", "slot-1"))
            .ReturnsAsync(sourceSlot);
        _mockSlotRepo
            .Setup(x => x.GetSlotAsync("league-1", "10U", "slot-2"))
            .ReturnsAsync(targetSlot);
        _mockTeamRepo
            .Setup(x => x.GetTeamAsync("league-1", "10U", "Panthers"))
            .ReturnsAsync(new TableEntity("TEAM|league-1|10U", "Panthers"));
        _mockPracticeRequestRepo
            .Setup(x => x.QueryRequestsAsync("league-1", null, "10U", "Panthers", null))
            .ReturnsAsync(new List<TableEntity> { sourceRequest });
        _mockPracticeRequestRepo
            .Setup(x => x.QuerySlotRequestsAsync("league-1", "10U", "slot-2", It.IsAny<IReadOnlyCollection<string>>()))
            .ReturnsAsync(new List<TableEntity>());
        _mockSlotRepo
            .Setup(x => x.UpdateSlotAsync(It.IsAny<TableEntity>(), It.IsAny<ETag>()))
            .Returns(Task.CompletedTask);
        _mockPracticeRequestRepo
            .Setup(x => x.CreateRequestAsync(It.IsAny<TableEntity>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.CreateMoveRequestAsync(
            leagueId: "league-1",
            userId: "coach-1",
            sourceRequestId: "req-1",
            targetSlotId: "slot-2",
            reason: "Moving to better time",
            openToShareField: false,
            shareWithTeamId: null);

        // Assert - should succeed since outside lead time
        Assert.NotNull(result);
        Assert.Equal("Move", result.GetString("RequestKind"));
        _mockPracticeRequestRepo.Verify(x => x.CreateRequestAsync(It.IsAny<TableEntity>()), Times.Once);
    }

    private static TableEntity BuildMembership(string role, string division, string teamId)
    {
        return new TableEntity("user-1", "league-1")
        {
            ["Role"] = role,
            ["Division"] = division,
            ["TeamId"] = teamId
        };
    }
}
