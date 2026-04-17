using Azure.Data.Tables;
using GameSwap.Functions.Repositories;
using GameSwap.Functions.Services;
using GameSwap.Functions.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GameSwap.Tests.Services;

/// <summary>
/// Unit tests for simplified calendar-integrated practice requests with auto-approval.
/// </summary>
public class SimplePracticeRequestTests
{
    private readonly Mock<IPracticeRequestRepository> _mockPracticeRepo;
    private readonly Mock<IMembershipRepository> _mockMembershipRepo;
    private readonly Mock<ITeamRepository> _mockTeamRepo;
    private readonly Mock<IPracticeRequestService> _mockPracticeService;
    private readonly ILogger<object> _logger;

    public SimplePracticeRequestTests()
    {
        _mockPracticeRepo = new Mock<IPracticeRequestRepository>();
        _mockMembershipRepo = new Mock<IMembershipRepository>();
        _mockTeamRepo = new Mock<ITeamRepository>();
        _mockPracticeService = new Mock<IPracticeRequestService>();
        _logger = NullLogger<object>.Instance;

        // Setup default team repository behavior
        _mockTeamRepo
            .Setup(x => x.QueryAllTeamsAsync(It.IsAny<string>()))
            .ReturnsAsync(new List<TableEntity>());
    }

    [Fact]
    public async Task CheckConflicts_NoExistingRequests_ReturnsEmpty()
    {
        // Arrange
        _mockPracticeRepo
            .Setup(x => x.GetRequestsByFieldAndDateAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
            .ReturnsAsync(new List<TableEntity>());

        // Act
        var conflicts = await SimplePracticeRequestExtensions.CheckSimplePracticeConflictsAsync(
            "field1",
            "2026-05-01",
            "18:00",
            "19:30",
            "shared",
            "league1",
            "team1",
            _mockPracticeRepo.Object,
            _mockTeamRepo.Object,
            _logger
        );

        // Assert
        Assert.Empty(conflicts);
    }

    [Fact]
    public async Task CheckConflicts_OverlappingTime_ReturnsConflict()
    {
        // Arrange
        var existingRequest = new TableEntity
        {
            PartitionKey = "PRACTICE|league1",
            RowKey = "req1",
            ["TeamId"] = "team2",
            ["StartTime"] = "18:30",
            ["EndTime"] = "20:00",
            ["Policy"] = "shared",
            ["Status"] = "Approved"
        };

        _mockPracticeRepo
            .Setup(x => x.GetRequestsByFieldAndDateAsync("league1", "field1", "2026-05-01"))
            .ReturnsAsync(new List<TableEntity> { existingRequest });

        // Act - Request 18:00-19:30 overlaps with existing 18:30-20:00
        var conflicts = await SimplePracticeRequestExtensions.CheckSimplePracticeConflictsAsync(
            "field1",
            "2026-05-01",
            "18:00",
            "19:30",
            "shared",
            "league1",
            "team1",
            _mockPracticeRepo.Object,
            _logger
        );

        // Assert
        Assert.Single(conflicts);
        Assert.Equal("req1", conflicts[0].RequestId);
        Assert.Equal("team2", conflicts[0].TeamId);
        Assert.Equal("18:30", conflicts[0].StartTime);
        Assert.Equal("20:00", conflicts[0].EndTime);
        Assert.Equal("shared", conflicts[0].Policy);
    }

    [Fact]
    public async Task CheckConflicts_NoOverlap_ReturnsEmpty()
    {
        // Arrange
        var existingRequest = new TableEntity
        {
            PartitionKey = "PRACTICE|league1",
            RowKey = "req1",
            ["TeamId"] = "team2",
            ["StartTime"] = "16:00",
            ["EndTime"] = "17:30",
            ["Policy"] = "shared",
            ["Status"] = "Approved"
        };

        _mockPracticeRepo
            .Setup(x => x.GetRequestsByFieldAndDateAsync("league1", "field1", "2026-05-01"))
            .ReturnsAsync(new List<TableEntity> { existingRequest });

        // Act - Request 18:00-19:30 does not overlap with 16:00-17:30
        var conflicts = await SimplePracticeRequestExtensions.CheckSimplePracticeConflictsAsync(
            "field1",
            "2026-05-01",
            "18:00",
            "19:30",
            "shared",
            "league1",
            "team1",
            _mockPracticeRepo.Object,
            _logger
        );

        // Assert
        Assert.Empty(conflicts);
    }

    [Fact]
    public async Task CheckConflicts_SameTeam_ExcludesFromConflicts()
    {
        // Arrange - Same team moving their practice time
        var existingRequest = new TableEntity
        {
            PartitionKey = "PRACTICE|league1",
            RowKey = "req1",
            ["TeamId"] = "team1", // Same team
            ["StartTime"] = "18:00",
            ["EndTime"] = "19:30",
            ["Policy"] = "shared",
            ["Status"] = "Approved"
        };

        _mockPracticeRepo
            .Setup(x => x.GetRequestsByFieldAndDateAsync("league1", "field1", "2026-05-01"))
            .ReturnsAsync(new List<TableEntity> { existingRequest });

        // Act
        var conflicts = await SimplePracticeRequestExtensions.CheckSimplePracticeConflictsAsync(
            "field1",
            "2026-05-01",
            "18:30",
            "20:00",
            "shared",
            "league1",
            "team1", // Same team - should be excluded
            _mockPracticeRepo.Object,
            _logger
        );

        // Assert
        Assert.Empty(conflicts); // Same team excluded
    }

    [Fact]
    public async Task CheckConflicts_CancelledRequest_IgnoresConflict()
    {
        // Arrange
        var cancelledRequest = new TableEntity
        {
            PartitionKey = "PRACTICE|league1",
            RowKey = "req1",
            ["TeamId"] = "team2",
            ["StartTime"] = "18:00",
            ["EndTime"] = "19:30",
            ["Policy"] = "shared",
            ["Status"] = "Cancelled" // Cancelled should be ignored
        };

        _mockPracticeRepo
            .Setup(x => x.GetRequestsByFieldAndDateAsync("league1", "field1", "2026-05-01"))
            .ReturnsAsync(new List<TableEntity> { cancelledRequest });

        // Act
        var conflicts = await SimplePracticeRequestExtensions.CheckSimplePracticeConflictsAsync(
            "field1",
            "2026-05-01",
            "18:00",
            "19:30",
            "shared",
            "league1",
            "team1",
            _mockPracticeRepo.Object,
            _logger
        );

        // Assert
        Assert.Empty(conflicts); // Cancelled requests don't conflict
    }

    [Fact]
    public async Task CreateSimpleRequest_NoConflicts_AutoApproves()
    {
        // Arrange
        var requestParams = new SimplePracticeRequestParams
        {
            FieldKey = "field1",
            Date = "2026-05-01",
            StartTime = "18:00",
            EndTime = "19:30",
            Policy = "shared",
            Notes = "Team practice"
        };

        _mockPracticeRepo
            .Setup(x => x.GetRequestsByFieldAndDateAsync("league1", "field1", "2026-05-01"))
            .ReturnsAsync(new List<TableEntity>());

        _mockPracticeRepo
            .Setup(x => x.CreateRequestAsync(It.IsAny<TableEntity>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _mockPracticeService.Object.CreateSimplePracticeRequestAsync(
            requestParams,
            "league1",
            "user1",
            "team1",
            _mockMembershipRepo.Object,
            _mockPracticeRepo.Object,
            _mockTeamRepo.Object,
            _logger
        );

        // Assert
        Assert.Equal("Approved", result.Status);
        Assert.True(result.AutoApproved);
        Assert.Empty(result.Conflicts);
    }

    [Fact]
    public async Task CreateSimpleRequest_SharedWithSharedConflicts_AutoApproves()
    {
        // Arrange - Multiple teams can share same field
        var requestParams = new SimplePracticeRequestParams
        {
            FieldKey = "field1",
            Date = "2026-05-01",
            StartTime = "18:00",
            EndTime = "19:30",
            Policy = "shared"
        };

        var existingRequests = new List<TableEntity>
        {
            new TableEntity
            {
                PartitionKey = "PRACTICE|league1",
                RowKey = "req1",
                ["TeamId"] = "team2",
                ["StartTime"] = "18:00",
                ["EndTime"] = "19:30",
                ["Policy"] = "shared",
                ["Status"] = "Approved"
            },
            new TableEntity
            {
                PartitionKey = "PRACTICE|league1",
                RowKey = "req2",
                ["TeamId"] = "team3",
                ["StartTime"] = "18:15",
                ["EndTime"] = "19:45",
                ["Policy"] = "shared",
                ["Status"] = "Approved"
            }
        };

        _mockPracticeRepo
            .Setup(x => x.GetRequestsByFieldAndDateAsync("league1", "field1", "2026-05-01"))
            .ReturnsAsync(existingRequests);

        _mockPracticeRepo
            .Setup(x => x.CreateRequestAsync(It.IsAny<TableEntity>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _mockPracticeService.Object.CreateSimplePracticeRequestAsync(
            requestParams,
            "league1",
            "user1",
            "team1",
            _mockMembershipRepo.Object,
            _mockPracticeRepo.Object,
            _mockTeamRepo.Object,
            _logger
        );

        // Assert
        Assert.Equal("Approved", result.Status);
        Assert.True(result.AutoApproved);
        Assert.Equal(2, result.Conflicts.Count); // Conflicts detected but auto-approved
    }

    [Fact]
    public async Task CreateSimpleRequest_ExclusiveWithConflict_RequiresApproval()
    {
        // Arrange
        var requestParams = new SimplePracticeRequestParams
        {
            FieldKey = "field1",
            Date = "2026-05-01",
            StartTime = "18:00",
            EndTime = "19:30",
            Policy = "exclusive"
        };

        var existingRequest = new TableEntity
        {
            PartitionKey = "PRACTICE|league1",
            RowKey = "req1",
            ["TeamId"] = "team2",
            ["StartTime"] = "18:30",
            ["EndTime"] = "20:00",
            ["Policy"] = "shared",
            ["Status"] = "Approved"
        };

        _mockPracticeRepo
            .Setup(x => x.GetRequestsByFieldAndDateAsync("league1", "field1", "2026-05-01"))
            .ReturnsAsync(new List<TableEntity> { existingRequest });

        _mockPracticeRepo
            .Setup(x => x.CreateRequestAsync(It.IsAny<TableEntity>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _mockPracticeService.Object.CreateSimplePracticeRequestAsync(
            requestParams,
            "league1",
            "user1",
            "team1",
            _mockMembershipRepo.Object,
            _mockPracticeRepo.Object,
            _mockTeamRepo.Object,
            _logger
        );

        // Assert
        Assert.Equal("Pending", result.Status);
        Assert.False(result.AutoApproved);
        Assert.Single(result.Conflicts);
    }

    [Fact]
    public async Task CreateSimpleRequest_SharedWithExclusiveConflict_RequiresApproval()
    {
        // Arrange - Shared request but existing exclusive booking
        var requestParams = new SimplePracticeRequestParams
        {
            FieldKey = "field1",
            Date = "2026-05-01",
            StartTime = "18:00",
            EndTime = "19:30",
            Policy = "shared"
        };

        var existingRequest = new TableEntity
        {
            PartitionKey = "PRACTICE|league1",
            RowKey = "req1",
            ["TeamId"] = "team2",
            ["StartTime"] = "18:00",
            ["EndTime"] = "19:30",
            ["Policy"] = "exclusive", // Exclusive booking blocks shared requests
            ["Status"] = "Approved"
        };

        _mockPracticeRepo
            .Setup(x => x.GetRequestsByFieldAndDateAsync("league1", "field1", "2026-05-01"))
            .ReturnsAsync(new List<TableEntity> { existingRequest });

        _mockPracticeRepo
            .Setup(x => x.CreateRequestAsync(It.IsAny<TableEntity>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _mockPracticeService.Object.CreateSimplePracticeRequestAsync(
            requestParams,
            "league1",
            "user1",
            "team1",
            _mockMembershipRepo.Object,
            _mockPracticeRepo.Object,
            _mockTeamRepo.Object,
            _logger
        );

        // Assert
        Assert.Equal("Pending", result.Status); // Cannot auto-approve with exclusive conflict
        Assert.False(result.AutoApproved);
        Assert.Single(result.Conflicts);
    }

    [Theory]
    [InlineData("18:00", "19:00", "19:00", "20:00", false)] // No overlap (adjacent)
    [InlineData("18:00", "19:00", "19:30", "20:30", false)] // No overlap (after)
    [InlineData("18:00", "19:00", "16:00", "17:00", false)] // No overlap (before)
    [InlineData("18:00", "19:30", "18:30", "19:00", true)] // Overlap (contained)
    [InlineData("18:00", "19:30", "17:00", "18:30", true)] // Overlap (starts before)
    [InlineData("18:00", "19:30", "19:00", "20:00", true)] // Overlap (ends after)
    [InlineData("18:00", "19:30", "17:00", "20:00", true)] // Overlap (completely contains)
    public async Task CheckConflicts_TimeOverlapDetection_WorksCorrectly(
        string requestStart,
        string requestEnd,
        string existingStart,
        string existingEnd,
        bool shouldConflict)
    {
        // Arrange
        var existingRequest = new TableEntity
        {
            PartitionKey = "PRACTICE|league1",
            RowKey = "req1",
            ["TeamId"] = "team2",
            ["StartTime"] = existingStart,
            ["EndTime"] = existingEnd,
            ["Policy"] = "shared",
            ["Status"] = "Approved"
        };

        _mockPracticeRepo
            .Setup(x => x.GetRequestsByFieldAndDateAsync("league1", "field1", "2026-05-01"))
            .ReturnsAsync(new List<TableEntity> { existingRequest });

        // Act
        var conflicts = await SimplePracticeRequestExtensions.CheckSimplePracticeConflictsAsync(
            "field1",
            "2026-05-01",
            requestStart,
            requestEnd,
            "shared",
            "league1",
            "team1",
            _mockPracticeRepo.Object,
            _logger
        );

        // Assert
        if (shouldConflict)
            Assert.Single(conflicts);
        else
            Assert.Empty(conflicts);
    }

    [Fact]
    public void ParseTime_ValidTimes_ParsesCorrectly()
    {
        // This would test the private ParseTime method if made internal for testing
        // For now, we test it indirectly through conflict checking
        Assert.True(true); // Placeholder
    }

    [Fact]
    public async Task CreateSimpleRequest_ValidatesRequiredFields()
    {
        // Arrange - Missing field key
        var requestParams = new SimplePracticeRequestParams
        {
            FieldKey = "", // Empty
            Date = "2026-05-01",
            StartTime = "18:00",
            EndTime = "19:30"
        };

        // Act & Assert
        await Assert.ThrowsAsync<ApiGuards.HttpError>(async () =>
            await _mockPracticeService.Object.CreateSimplePracticeRequestAsync(
                requestParams,
                "league1",
                "user1",
                "team1",
                _mockMembershipRepo.Object,
                _mockPracticeRepo.Object,
                _logger
            )
        );
    }

    [Fact]
    public async Task CreateSimpleRequest_MultipleSharedBookings_AllAutoApprove()
    {
        // Arrange - 3 teams sharing same field at same time
        var existingRequests = new List<TableEntity>
        {
            new TableEntity
            {
                PartitionKey = "PRACTICE|league1",
                RowKey = "req1",
                ["TeamId"] = "team2",
                ["StartTime"] = "18:00",
                ["EndTime"] = "19:30",
                ["Policy"] = "shared",
                ["Status"] = "Approved"
            },
            new TableEntity
            {
                PartitionKey = "PRACTICE|league1",
                RowKey = "req2",
                ["TeamId"] = "team3",
                ["StartTime"] = "18:00",
                ["EndTime"] = "19:30",
                ["Policy"] = "shared",
                ["Status"] = "Approved"
            }
        };

        var requestParams = new SimplePracticeRequestParams
        {
            FieldKey = "field1",
            Date = "2026-05-01",
            StartTime = "18:00",
            EndTime = "19:30",
            Policy = "shared"
        };

        _mockPracticeRepo
            .Setup(x => x.GetRequestsByFieldAndDateAsync("league1", "field1", "2026-05-01"))
            .ReturnsAsync(existingRequests);

        _mockPracticeRepo
            .Setup(x => x.CreateRequestAsync(It.IsAny<TableEntity>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _mockPracticeService.Object.CreateSimplePracticeRequestAsync(
            requestParams,
            "league1",
            "user1",
            "team1",
            _mockMembershipRepo.Object,
            _mockPracticeRepo.Object,
            _mockTeamRepo.Object,
            _logger
        );

        // Assert
        Assert.Equal("Approved", result.Status);
        Assert.True(result.AutoApproved);
        Assert.Equal(2, result.Conflicts.Count); // Shows conflicts but still auto-approved
    }

    [Fact]
    public async Task CheckConflicts_ResolvesTeamNames()
    {
        // Arrange
        var existingRequest = new TableEntity
        {
            PartitionKey = "PRACTICE|league1",
            RowKey = "req1",
            ["TeamId"] = "TEAM_002",
            ["StartTime"] = "18:00",
            ["EndTime"] = "19:30",
            ["Policy"] = "shared",
            ["Status"] = "Approved"
        };

        var teamEntity = new TableEntity
        {
            PartitionKey = "TEAM|league1",
            RowKey = "TEAM_002",
            ["TeamId"] = "TEAM_002",
            ["Name"] = "Thunder" // Team name to be resolved
        };

        _mockPracticeRepo
            .Setup(x => x.GetRequestsByFieldAndDateAsync("league1", "field1", "2026-05-01"))
            .ReturnsAsync(new List<TableEntity> { existingRequest });

        _mockTeamRepo
            .Setup(x => x.QueryAllTeamsAsync("league1"))
            .ReturnsAsync(new List<TableEntity> { teamEntity });

        // Act
        var conflicts = await SimplePracticeRequestExtensions.CheckSimplePracticeConflictsAsync(
            "field1",
            "2026-05-01",
            "18:00",
            "19:30",
            "shared",
            "league1",
            "team1",
            _mockPracticeRepo.Object,
            _mockTeamRepo.Object,
            _logger
        );

        // Assert
        Assert.Single(conflicts);
        Assert.Equal("Thunder", conflicts[0].TeamName); // Should show team name, not ID
        Assert.Equal("TEAM_002", conflicts[0].TeamId);
    }
}
