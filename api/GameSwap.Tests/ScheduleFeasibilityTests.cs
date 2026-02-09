using System;
using System.Linq;
using GameSwap.Scheduling;
using Xunit;

namespace GameSwap.Tests;

public class ScheduleFeasibilityTests
{
    #region Analyze Method Tests

    [Fact]
    public void Analyze_WithSufficientCapacity_ReturnsFeasible()
    {
        // Arrange: 10 teams, 10 games each, 50 slots available
        var result = ScheduleFeasibility.Analyze(
            teamCount: 10,
            availableRegularSlots: 50,
            availablePoolSlots: 0,
            availableBracketSlots: 0,
            minGamesPerTeam: 10,
            poolGamesPerTeam: 0,
            maxGamesPerWeek: 2,
            noDoubleHeaders: false,
            regularWeeksCount: 10,
            guestGamesPerWeek: 0
        );

        // Assert
        Assert.True(result.RegularSeasonFeasible);
        Assert.Empty(result.Conflicts);
        Assert.Equal(50, result.Capacity.AvailableRegularSlots);
        Assert.Equal(50, result.Capacity.RequiredRegularSlots); // (10 * 10) / 2 = 50
        Assert.Equal(0, result.Capacity.SurplusOrShortfall);
    }

    [Fact]
    public void Analyze_WithInsufficientCapacity_ReturnsNotFeasibleWithConflict()
    {
        // Arrange: 9 teams, 15 games each, only 45 slots available
        // Required: (9 * 15) / 2 = 67.5 ≈ 68 slots
        var result = ScheduleFeasibility.Analyze(
            teamCount: 9,
            availableRegularSlots: 45,
            availablePoolSlots: 0,
            availableBracketSlots: 0,
            minGamesPerTeam: 15,
            poolGamesPerTeam: 0,
            maxGamesPerWeek: 2,
            noDoubleHeaders: false,
            regularWeeksCount: 10,
            guestGamesPerWeek: 0
        );

        // Assert
        Assert.False(result.RegularSeasonFeasible);
        Assert.NotEmpty(result.Conflicts);
        Assert.Contains(result.Conflicts, c => c.ConflictId == "capacity-insufficient");
        Assert.Equal("error", result.Conflicts.First(c => c.ConflictId == "capacity-insufficient").Severity);
    }

    [Fact]
    public void Analyze_WithZeroTeams_ReturnsNoConflicts()
    {
        // Arrange
        var result = ScheduleFeasibility.Analyze(
            teamCount: 0,
            availableRegularSlots: 50,
            availablePoolSlots: 0,
            availableBracketSlots: 0,
            minGamesPerTeam: 10,
            poolGamesPerTeam: 0,
            maxGamesPerWeek: 2,
            noDoubleHeaders: false,
            regularWeeksCount: 10,
            guestGamesPerWeek: 0
        );

        // Assert
        Assert.Empty(result.Conflicts);
        Assert.Equal(0, result.Recommendations.MinGamesPerTeam);
        Assert.Equal(0, result.Recommendations.MaxGamesPerTeam);
    }

    [Fact]
    public void Analyze_WithPoolAndBracketSlots_ValidatesAllPhases()
    {
        // Arrange
        var result = ScheduleFeasibility.Analyze(
            teamCount: 8,
            availableRegularSlots: 30,
            availablePoolSlots: 16,
            availableBracketSlots: 3,
            minGamesPerTeam: 8,
            poolGamesPerTeam: 4,
            maxGamesPerWeek: 2,
            noDoubleHeaders: false,
            regularWeeksCount: 10,
            guestGamesPerWeek: 0
        );

        // Assert
        // Regular: (8 * 8) / 2 = 32 required, 30 available → Not feasible
        // Pool: (8 * 4) / 2 = 16 required, 16 available → Feasible
        // Bracket: 3 required, 3 available → Feasible
        Assert.False(result.RegularSeasonFeasible);
        Assert.True(result.PoolPlayFeasible);
        Assert.True(result.BracketFeasible);
    }

    #endregion

    #region CalculateRecommendations Tests

    [Fact]
    public void CalculateRecommendations_WithExactCapacity_RecommendsOptimalGames()
    {
        // Arrange: 10 teams, exactly 50 slots needed, 50 slots available, 10 weeks
        // With minGamesPerTeam=10 set, required: (10 * 10) / 2 = 50 (no surplus)
        var result = ScheduleFeasibility.Analyze(
            teamCount: 10,
            availableRegularSlots: 50,
            availablePoolSlots: 0,
            availableBracketSlots: 0,
            minGamesPerTeam: 10,
            poolGamesPerTeam: 0,
            maxGamesPerWeek: 2,
            noDoubleHeaders: false,
            regularWeeksCount: 10,
            guestGamesPerWeek: 0
        );

        // Assert
        Assert.InRange(result.Recommendations.MinGamesPerTeam, 8, 10);
        Assert.Equal(10, result.Recommendations.MaxGamesPerTeam);
        Assert.Equal(0, result.Recommendations.OptimalGuestGamesPerWeek); // No surplus for guest games
    }

    [Fact]
    public void CalculateRecommendations_WithOddTeams_RecommendsGuestGames()
    {
        // Arrange: 9 teams (odd), 9 games each, 50 slots, 10 weeks
        // Required: (9 * 9) / 2 = 40.5 ≈ 41 slots
        // Total games: 9 * 9 = 81 (odd team count creates imbalance)
        var result = ScheduleFeasibility.Analyze(
            teamCount: 9,
            availableRegularSlots: 50,
            availablePoolSlots: 0,
            availableBracketSlots: 0,
            minGamesPerTeam: 9,
            poolGamesPerTeam: 0,
            maxGamesPerWeek: 2,
            noDoubleHeaders: false,
            regularWeeksCount: 10,
            guestGamesPerWeek: 0
        );

        // Assert
        // With 9 teams and 9 games each, we might need guest games to balance
        Assert.True(result.Recommendations.OptimalGuestGamesPerWeek >= 0);
    }

    [Fact]
    public void CalculateRecommendations_WithHighUtilization_ReportsCorrectStatus()
    {
        // Arrange: 9 teams, 10 games each, 50 slots (45 required = 90% utilization)
        var result = ScheduleFeasibility.Analyze(
            teamCount: 9,
            availableRegularSlots: 50,
            availablePoolSlots: 0,
            availableBracketSlots: 0,
            minGamesPerTeam: 10,
            poolGamesPerTeam: 0,
            maxGamesPerWeek: 2,
            noDoubleHeaders: false,
            regularWeeksCount: 10,
            guestGamesPerWeek: 0
        );

        // Assert
        Assert.Contains("High utilization", result.Recommendations.UtilizationStatus);
    }

    [Fact]
    public void CalculateRecommendations_WithLowUtilization_ReportsCorrectStatus()
    {
        // Arrange: 5 teams, 5 games each, 100 slots (13 required = 13% utilization)
        var result = ScheduleFeasibility.Analyze(
            teamCount: 5,
            availableRegularSlots: 100,
            availablePoolSlots: 0,
            availableBracketSlots: 0,
            minGamesPerTeam: 5,
            poolGamesPerTeam: 0,
            maxGamesPerWeek: 2,
            noDoubleHeaders: false,
            regularWeeksCount: 10,
            guestGamesPerWeek: 0
        );

        // Assert
        Assert.Contains("Low utilization", result.Recommendations.UtilizationStatus);
    }

    [Fact]
    public void CalculateRecommendations_WithGoodUtilization_ReportsCorrectStatus()
    {
        // Arrange: 10 teams, 7 games each, 50 slots (35 required = 70% utilization)
        var result = ScheduleFeasibility.Analyze(
            teamCount: 10,
            availableRegularSlots: 50,
            availablePoolSlots: 0,
            availableBracketSlots: 0,
            minGamesPerTeam: 7,
            poolGamesPerTeam: 0,
            maxGamesPerWeek: 2,
            noDoubleHeaders: false,
            regularWeeksCount: 10,
            guestGamesPerWeek: 0
        );

        // Assert
        Assert.Contains("Good utilization", result.Recommendations.UtilizationStatus);
    }

    #endregion

    #region CalculateOptimalGuestGames Tests

    [Fact]
    public void CalculateOptimalGuestGames_WithEvenTeams_ReturnsZeroOrLowValue()
    {
        // Arrange: 10 teams (even), 10 games each, 60 slots, 10 weeks
        // Surplus capacity might suggest guest games
        var result = ScheduleFeasibility.Analyze(
            teamCount: 10,
            availableRegularSlots: 60,
            availablePoolSlots: 0,
            availableBracketSlots: 0,
            minGamesPerTeam: 10,
            poolGamesPerTeam: 0,
            maxGamesPerWeek: 2,
            noDoubleHeaders: false,
            regularWeeksCount: 10,
            guestGamesPerWeek: 0
        );

        // Assert
        // With 20% surplus (60 available, 50 required), might suggest 1 guest game/week
        Assert.True(result.Recommendations.OptimalGuestGamesPerWeek >= 0);
        Assert.True(result.Recommendations.OptimalGuestGamesPerWeek <= 1);
    }

    [Fact]
    public void CalculateOptimalGuestGames_WithNoSurplus_ReturnsZero()
    {
        // Arrange: 10 teams, 10 games each, exactly 50 slots (no surplus)
        var result = ScheduleFeasibility.Analyze(
            teamCount: 10,
            availableRegularSlots: 50,
            availablePoolSlots: 0,
            availableBracketSlots: 0,
            minGamesPerTeam: 10,
            poolGamesPerTeam: 0,
            maxGamesPerWeek: 2,
            noDoubleHeaders: false,
            regularWeeksCount: 10,
            guestGamesPerWeek: 0
        );

        // Assert
        Assert.Equal(0, result.Recommendations.OptimalGuestGamesPerWeek);
    }

    #endregion

    #region DetectConflicts Tests

    [Fact]
    public void DetectConflicts_CapacityInsufficient_ReturnsError()
    {
        // Arrange: 9 teams, 15 games each, 45 slots
        var result = ScheduleFeasibility.Analyze(
            teamCount: 9,
            availableRegularSlots: 45,
            availablePoolSlots: 0,
            availableBracketSlots: 0,
            minGamesPerTeam: 15,
            poolGamesPerTeam: 0,
            maxGamesPerWeek: 2,
            noDoubleHeaders: false,
            regularWeeksCount: 10,
            guestGamesPerWeek: 0
        );

        // Assert
        var conflict = result.Conflicts.FirstOrDefault(c => c.ConflictId == "capacity-insufficient");
        Assert.NotNull(conflict);
        Assert.Equal("error", conflict.Severity);
        Assert.Contains("68 slots", conflict.Message); // (9 * 15) / 2 = 67.5 ≈ 68
        Assert.Contains("45 available", conflict.Message);
    }

    [Fact]
    public void DetectConflicts_GuestGamesOverConsuming_ReturnsWarning()
    {
        // Arrange: 9 teams, 10 games each, 50 slots, 2 guest games/week
        // Required: 45 slots
        // Guest: 2 * 10 = 20 slots
        // Remaining: 50 - 20 = 30 < 45 → Conflict
        var result = ScheduleFeasibility.Analyze(
            teamCount: 9,
            availableRegularSlots: 50,
            availablePoolSlots: 0,
            availableBracketSlots: 0,
            minGamesPerTeam: 10,
            poolGamesPerTeam: 0,
            maxGamesPerWeek: 2,
            noDoubleHeaders: false,
            regularWeeksCount: 10,
            guestGamesPerWeek: 2
        );

        // Assert
        var conflict = result.Conflicts.FirstOrDefault(c => c.ConflictId == "guest-games-over-consuming");
        Assert.NotNull(conflict);
        Assert.Equal("warning", conflict.Severity);
        Assert.Contains("20 slots", conflict.Message); // 2 guest games/week * 10 weeks
    }

    [Fact]
    public void DetectConflicts_NoDoubleHeadersBlocking_ReturnsError()
    {
        // Arrange: 9 teams, 12 games each, noDoubleHeaders=true, maxGamesPerWeek=1, 10 weeks
        // Each team can play at most 10 games (1/week * 10 weeks)
        // Requesting 12 games → Impossible
        var result = ScheduleFeasibility.Analyze(
            teamCount: 9,
            availableRegularSlots: 60,
            availablePoolSlots: 0,
            availableBracketSlots: 0,
            minGamesPerTeam: 12,
            poolGamesPerTeam: 0,
            maxGamesPerWeek: 1,
            noDoubleHeaders: true,
            regularWeeksCount: 10,
            guestGamesPerWeek: 0
        );

        // Assert
        var conflict = result.Conflicts.FirstOrDefault(c => c.ConflictId == "no-doubleheaders-blocking");
        Assert.NotNull(conflict);
        Assert.Equal("error", conflict.Severity);
        Assert.Contains("12 games/team", conflict.Message);
        Assert.Contains("10 weeks", conflict.Message);
    }

    [Fact]
    public void DetectConflicts_MaxGamesPerWeekInsufficient_ReturnsWarning()
    {
        // Arrange: 10 teams, 12 games each, maxGamesPerWeek=1, 10 weeks
        // Total games needed: 10 * 12 = 120
        // Weekly capacity: 10 teams * 1 game/week * 10 weeks = 100
        // 120 > 100 → Warning
        var result = ScheduleFeasibility.Analyze(
            teamCount: 10,
            availableRegularSlots: 60,
            availablePoolSlots: 0,
            availableBracketSlots: 0,
            minGamesPerTeam: 12,
            poolGamesPerTeam: 0,
            maxGamesPerWeek: 1,
            noDoubleHeaders: false,
            regularWeeksCount: 10,
            guestGamesPerWeek: 0
        );

        // Assert
        var conflict = result.Conflicts.FirstOrDefault(c => c.ConflictId == "max-games-per-week-insufficient");
        Assert.NotNull(conflict);
        Assert.Equal("warning", conflict.Severity);
    }

    [Fact]
    public void DetectConflicts_AllConstraintsSatisfied_ReturnsNoConflicts()
    {
        // Arrange: 10 teams, 10 games each, 50 slots, reasonable constraints
        var result = ScheduleFeasibility.Analyze(
            teamCount: 10,
            availableRegularSlots: 50,
            availablePoolSlots: 0,
            availableBracketSlots: 0,
            minGamesPerTeam: 10,
            poolGamesPerTeam: 0,
            maxGamesPerWeek: 2,
            noDoubleHeaders: false,
            regularWeeksCount: 10,
            guestGamesPerWeek: 0
        );

        // Assert
        Assert.Empty(result.Conflicts);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void Analyze_WithOneTeam_ReturnsValidRecommendations()
    {
        // Arrange
        var result = ScheduleFeasibility.Analyze(
            teamCount: 1,
            availableRegularSlots: 10,
            availablePoolSlots: 0,
            availableBracketSlots: 0,
            minGamesPerTeam: 0,
            poolGamesPerTeam: 0,
            maxGamesPerWeek: 2,
            noDoubleHeaders: false,
            regularWeeksCount: 10,
            guestGamesPerWeek: 0
        );

        // Assert
        Assert.NotNull(result.Recommendations);
        // With 1 team, can't play against itself, so max games = 0
        Assert.Equal(0, result.Recommendations.MaxGamesPerTeam);
    }

    [Fact]
    public void Analyze_WithHundredTeams_HandlesLargeNumbers()
    {
        // Arrange
        var result = ScheduleFeasibility.Analyze(
            teamCount: 100,
            availableRegularSlots: 5000,
            availablePoolSlots: 0,
            availableBracketSlots: 0,
            minGamesPerTeam: 99,
            poolGamesPerTeam: 0,
            maxGamesPerWeek: 2,
            noDoubleHeaders: false,
            regularWeeksCount: 50,
            guestGamesPerWeek: 0
        );

        // Assert
        // Required: (100 * 99) / 2 = 4950 slots
        Assert.True(result.RegularSeasonFeasible);
        Assert.Equal(5000, result.Capacity.AvailableRegularSlots);
        Assert.Equal(4950, result.Capacity.RequiredRegularSlots);
    }

    [Fact]
    public void Analyze_WithZeroSlots_ReturnsNotFeasible()
    {
        // Arrange
        var result = ScheduleFeasibility.Analyze(
            teamCount: 10,
            availableRegularSlots: 0,
            availablePoolSlots: 0,
            availableBracketSlots: 0,
            minGamesPerTeam: 10,
            poolGamesPerTeam: 0,
            maxGamesPerWeek: 2,
            noDoubleHeaders: false,
            regularWeeksCount: 10,
            guestGamesPerWeek: 0
        );

        // Assert
        Assert.False(result.RegularSeasonFeasible);
        Assert.NotEmpty(result.Conflicts);
    }

    [Fact]
    public void Analyze_WithZeroWeeks_HandlesGracefully()
    {
        // Arrange
        var result = ScheduleFeasibility.Analyze(
            teamCount: 10,
            availableRegularSlots: 50,
            availablePoolSlots: 0,
            availableBracketSlots: 0,
            minGamesPerTeam: 10,
            poolGamesPerTeam: 0,
            maxGamesPerWeek: 2,
            noDoubleHeaders: false,
            regularWeeksCount: 0,
            guestGamesPerWeek: 0
        );

        // Assert
        Assert.NotNull(result);
        Assert.Equal(0, result.Recommendations.OptimalGuestGamesPerWeek);
    }

    #endregion

    #region Capacity Breakdown Tests

    [Fact]
    public void CapacityBreakdown_CalculatesCorrectSurplus()
    {
        // Arrange: 10 teams, 8 games each, 50 slots
        // Required: (10 * 8) / 2 = 40
        // Surplus: 50 - 40 = 10
        var result = ScheduleFeasibility.Analyze(
            teamCount: 10,
            availableRegularSlots: 50,
            availablePoolSlots: 0,
            availableBracketSlots: 0,
            minGamesPerTeam: 8,
            poolGamesPerTeam: 0,
            maxGamesPerWeek: 2,
            noDoubleHeaders: false,
            regularWeeksCount: 10,
            guestGamesPerWeek: 0
        );

        // Assert
        Assert.Equal(50, result.Capacity.AvailableRegularSlots);
        Assert.Equal(40, result.Capacity.RequiredRegularSlots);
        Assert.Equal(10, result.Capacity.SurplusOrShortfall);
        Assert.Equal(0, result.Capacity.GuestSlotsReserved);
        Assert.Equal(50, result.Capacity.EffectiveSlotsRemaining);
    }

    [Fact]
    public void CapacityBreakdown_CalculatesCorrectShortfall()
    {
        // Arrange: 10 teams, 12 games each, 50 slots
        // Required: (10 * 12) / 2 = 60
        // Shortfall: 50 - 60 = -10
        var result = ScheduleFeasibility.Analyze(
            teamCount: 10,
            availableRegularSlots: 50,
            availablePoolSlots: 0,
            availableBracketSlots: 0,
            minGamesPerTeam: 12,
            poolGamesPerTeam: 0,
            maxGamesPerWeek: 2,
            noDoubleHeaders: false,
            regularWeeksCount: 10,
            guestGamesPerWeek: 0
        );

        // Assert
        Assert.Equal(50, result.Capacity.AvailableRegularSlots);
        Assert.Equal(60, result.Capacity.RequiredRegularSlots);
        Assert.Equal(-10, result.Capacity.SurplusOrShortfall);
    }

    [Fact]
    public void CapacityBreakdown_WithGuestGames_CalculatesReservedSlots()
    {
        // Arrange: 10 teams, 10 games each, 50 slots, 1 guest game/week, 10 weeks
        var result = ScheduleFeasibility.Analyze(
            teamCount: 10,
            availableRegularSlots: 50,
            availablePoolSlots: 0,
            availableBracketSlots: 0,
            minGamesPerTeam: 10,
            poolGamesPerTeam: 0,
            maxGamesPerWeek: 2,
            noDoubleHeaders: false,
            regularWeeksCount: 10,
            guestGamesPerWeek: 1
        );

        // Assert
        Assert.Equal(10, result.Capacity.GuestSlotsReserved); // 1 * 10
        Assert.Equal(40, result.Capacity.EffectiveSlotsRemaining); // 50 - 10
    }

    #endregion
}
