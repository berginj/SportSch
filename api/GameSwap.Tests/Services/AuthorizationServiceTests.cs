using System;
using System.Threading.Tasks;
using Azure.Data.Tables;
using GameSwap.Functions.Repositories;
using GameSwap.Functions.Services;
using GameSwap.Functions.Storage;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GameSwap.Tests.Services;

public class AuthorizationServiceTests : IDisposable
{
    private readonly Mock<IMembershipRepository> _mockMembershipRepo;
    private readonly Mock<ISlotRepository> _mockSlotRepo;
    private readonly Mock<ILogger<AuthorizationService>> _mockLogger;
    private readonly AuthorizationService _service;

    public AuthorizationServiceTests()
    {
        _mockMembershipRepo = new Mock<IMembershipRepository>();
        _mockSlotRepo = new Mock<ISlotRepository>();
        _mockLogger = new Mock<ILogger<AuthorizationService>>();

        _service = new AuthorizationService(
            _mockMembershipRepo.Object,
            _mockSlotRepo.Object,
            _mockLogger.Object
        );
    }

    [Fact]
    public async Task GetUserRoleAsync_WithLeagueAdmin_ReturnsLeagueAdmin()
    {
        // Arrange
        var userId = "user-1";
        var leagueId = "league-1";

        var membership = new TableEntity("pk", "rk")
        {
            { "Role", Constants.Roles.LeagueAdmin }
        };

        _mockMembershipRepo
            .Setup(x => x.GetMembershipAsync(userId, leagueId))
            .ReturnsAsync(membership);

        // Act
        var role = await _service.GetUserRoleAsync(userId, leagueId);

        // Assert
        Assert.Equal(Constants.Roles.LeagueAdmin, role);
    }

    [Fact]
    public async Task GetUserRoleAsync_WithCoach_ReturnsCoach()
    {
        // Arrange
        var userId = "user-1";
        var leagueId = "league-1";

        var membership = new TableEntity("pk", "rk")
        {
            { "Role", Constants.Roles.Coach }
        };

        _mockMembershipRepo
            .Setup(x => x.GetMembershipAsync(userId, leagueId))
            .ReturnsAsync(membership);

        // Act
        var role = await _service.GetUserRoleAsync(userId, leagueId);

        // Assert
        Assert.Equal(Constants.Roles.Coach, role);
    }

    [Fact]
    public async Task GetUserRoleAsync_WithNoMembership_ReturnsViewer()
    {
        // Arrange
        var userId = "user-1";
        var leagueId = "league-1";

        _mockMembershipRepo
            .Setup(x => x.IsGlobalAdminAsync(userId))
            .ReturnsAsync(false);

        _mockMembershipRepo
            .Setup(x => x.GetMembershipAsync(userId, leagueId))
            .ReturnsAsync((TableEntity?)null);

        // Act
        var role = await _service.GetUserRoleAsync(userId, leagueId);

        // Assert
        Assert.Equal(Constants.Roles.Viewer, role);
    }

    [Fact]
    public async Task ValidateCoachAccessAsync_WithLeagueAdmin_Succeeds()
    {
        // Arrange
        var userId = "admin-user";
        var leagueId = "league-1";
        var division = "10U";
        var teamId = "team-1";

        var membership = new TableEntity("pk", "rk")
        {
            { "Role", Constants.Roles.LeagueAdmin }
        };

        _mockMembershipRepo
            .Setup(x => x.GetMembershipAsync(userId, leagueId))
            .ReturnsAsync(membership);

        // Act - Should not throw
        await _service.ValidateCoachAccessAsync(userId, leagueId, division, teamId);

        // Assert - Implicit success if no exception thrown
    }

    [Fact]
    public async Task ValidateCoachAccessAsync_WithMatchingCoach_Succeeds()
    {
        // Arrange
        var userId = "coach-user";
        var leagueId = "league-1";
        var division = "10U";
        var teamId = "team-1";

        var membership = new TableEntity("pk", "rk")
        {
            { "Role", Constants.Roles.Coach },
            { "CoachDivision", division },
            { "CoachTeamId", teamId }
        };

        _mockMembershipRepo
            .Setup(x => x.GetMembershipAsync(userId, leagueId))
            .ReturnsAsync(membership);

        // Act - Should not throw
        await _service.ValidateCoachAccessAsync(userId, leagueId, division, teamId);

        // Assert - Implicit success if no exception thrown
    }

    [Fact]
    public async Task ValidateCoachAccessAsync_WithCoachInWrongDivision_ThrowsForbidden()
    {
        // Arrange
        var userId = "coach-user";
        var leagueId = "league-1";
        var requestedDivision = "10U";
        var assignedDivision = "12U";
        var teamId = "team-1";

        var membership = new TableEntity("pk", "rk")
        {
            { "Role", Constants.Roles.Coach },
            { "CoachDivision", assignedDivision },
            { "CoachTeamId", teamId }
        };

        _mockMembershipRepo
            .Setup(x => x.GetMembershipAsync(userId, leagueId))
            .ReturnsAsync(membership);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ApiGuards.HttpError>(() =>
            _service.ValidateCoachAccessAsync(userId, leagueId, requestedDivision, teamId)
        );

        Assert.Equal(403, ex.Status);
        Assert.Equal(ErrorCodes.COACH_DIVISION_MISMATCH, ex.Code);
    }

    [Fact]
    public async Task ValidateCoachAccessAsync_WithCoachForWrongTeam_ThrowsForbidden()
    {
        // Arrange
        var userId = "coach-user";
        var leagueId = "league-1";
        var division = "10U";
        var requestedTeamId = "team-1";
        var assignedTeamId = "team-2";

        var membership = new TableEntity("pk", "rk")
        {
            { "Role", Constants.Roles.Coach },
            { "CoachDivision", division },
            { "CoachTeamId", assignedTeamId }
        };

        _mockMembershipRepo
            .Setup(x => x.GetMembershipAsync(userId, leagueId))
            .ReturnsAsync(membership);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ApiGuards.HttpError>(() =>
            _service.ValidateCoachAccessAsync(userId, leagueId, division, requestedTeamId)
        );

        Assert.Equal(403, ex.Status);
        Assert.Equal(ErrorCodes.UNAUTHORIZED, ex.Code);
    }

    [Fact]
    public async Task ValidateCoachAccessAsync_WithCoachButNoTeamId_ThrowsBadRequest()
    {
        // Arrange
        var userId = "coach-user";
        var leagueId = "league-1";
        var division = "10U";
        string? teamId = null;

        var membership = new TableEntity("pk", "rk")
        {
            { "Role", Constants.Roles.Coach },
            { "CoachDivision", division },
            { "CoachTeamId", "team-1" }
        };

        _mockMembershipRepo
            .Setup(x => x.GetMembershipAsync(userId, leagueId))
            .ReturnsAsync(membership);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ApiGuards.HttpError>(() =>
            _service.ValidateCoachAccessAsync(userId, leagueId, division, teamId)
        );

        Assert.Equal(400, ex.Status);
        Assert.Equal(ErrorCodes.COACH_TEAM_REQUIRED, ex.Code);
    }

    [Fact]
    public async Task ValidateCoachAccessAsync_WithViewer_ThrowsForbidden()
    {
        // Arrange
        var userId = "viewer-user";
        var leagueId = "league-1";
        var division = "10U";
        var teamId = "team-1";

        var membership = new TableEntity("pk", "rk")
        {
            { "Role", Constants.Roles.Viewer }
        };

        _mockMembershipRepo
            .Setup(x => x.GetMembershipAsync(userId, leagueId))
            .ReturnsAsync(membership);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ApiGuards.HttpError>(() =>
            _service.ValidateCoachAccessAsync(userId, leagueId, division, teamId)
        );

        Assert.Equal(403, ex.Status);
        Assert.Equal(ErrorCodes.FORBIDDEN, ex.Code);
    }

    [Fact]
    public async Task ValidateCoachAccessAsync_WithNoMembership_ThrowsForbidden()
    {
        // Arrange
        var userId = "unknown-user";
        var leagueId = "league-1";
        var division = "10U";
        var teamId = "team-1";

        _mockMembershipRepo
            .Setup(x => x.IsGlobalAdminAsync(userId))
            .ReturnsAsync(false);

        _mockMembershipRepo
            .Setup(x => x.GetMembershipAsync(userId, leagueId))
            .ReturnsAsync((TableEntity?)null);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ApiGuards.HttpError>(() =>
            _service.ValidateCoachAccessAsync(userId, leagueId, division, teamId)
        );

        Assert.Equal(403, ex.Status);
        Assert.Equal(ErrorCodes.FORBIDDEN, ex.Code);
    }

    [Fact]
    public async Task CanCancelSlotAsync_WithSlotOwner_ReturnsTrue()
    {
        // Arrange
        var userId = "user-1";
        var leagueId = "league-1";
        var offeringTeamId = "team-1";
        string? confirmedTeamId = null;

        var membership = new TableEntity("pk", "rk")
        {
            { "Role", Constants.Roles.Coach },
            { "CoachTeamId", offeringTeamId }
        };

        _mockMembershipRepo
            .Setup(x => x.GetMembershipAsync(userId, leagueId))
            .ReturnsAsync(membership);

        // Act
        var canCancel = await _service.CanCancelSlotAsync(userId, leagueId, offeringTeamId, confirmedTeamId);

        // Assert
        Assert.True(canCancel);
    }

    [Fact]
    public async Task CanCancelSlotAsync_WithLeagueAdmin_ReturnsTrue()
    {
        // Arrange
        var userId = "admin-user";
        var leagueId = "league-1";
        var offeringTeamId = "team-1";
        string? confirmedTeamId = null;

        var membership = new TableEntity("pk", "rk")
        {
            { "Role", Constants.Roles.LeagueAdmin }
        };

        _mockMembershipRepo
            .Setup(x => x.GetMembershipAsync(userId, leagueId))
            .ReturnsAsync(membership);

        // Act
        var canCancel = await _service.CanCancelSlotAsync(userId, leagueId, offeringTeamId, confirmedTeamId);

        // Assert
        Assert.True(canCancel);
    }

    [Fact]
    public async Task CanCancelSlotAsync_WithWrongTeamCoach_ReturnsFalse()
    {
        // Arrange
        var userId = "coach-user";
        var leagueId = "league-1";
        var offeringTeamId = "team-1";
        var userTeamId = "team-2";
        string? confirmedTeamId = null;

        var membership = new TableEntity("pk", "rk")
        {
            { "Role", Constants.Roles.Coach },
            { "CoachTeamId", userTeamId }
        };

        _mockMembershipRepo
            .Setup(x => x.GetMembershipAsync(userId, leagueId))
            .ReturnsAsync(membership);

        // Act
        var canCancel = await _service.CanCancelSlotAsync(userId, leagueId, offeringTeamId, confirmedTeamId);

        // Assert
        Assert.False(canCancel);
    }

    public void Dispose()
    {
        // Cleanup if needed
        GC.SuppressFinalize(this);
    }
}
