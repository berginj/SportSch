using System;
using System.Collections.Generic;
using System.Linq;

namespace GameSwap.Scheduling;

/// <summary>
/// Pre-scheduling feasibility analysis engine that calculates capacity,
/// detects constraint conflicts, and provides intelligent recommendations.
/// </summary>
public static class ScheduleFeasibility
{
    /// <summary>
    /// Analyzes scheduling feasibility and provides recommendations based on
    /// available slots, team count, and constraints.
    /// </summary>
    public static FeasibilityResult Analyze(
        int teamCount,
        int availableRegularSlots,
        int availablePoolSlots,
        int availableBracketSlots,
        int minGamesPerTeam,
        int poolGamesPerTeam,
        int maxGamesPerWeek,
        bool noDoubleHeaders,
        int regularWeeksCount,
        int guestGamesPerWeek)
    {
        // Calculate required slots for each phase
        int regularRequiredSlots = teamCount > 0 ? (int)Math.Ceiling((teamCount * minGamesPerTeam) / 2.0) : 0;
        int poolRequiredSlots = teamCount > 0 ? (int)Math.Ceiling((teamCount * poolGamesPerTeam) / 2.0) : 0;
        int bracketRequiredSlots = 3; // Always 3 games for bracket (SF1, SF2, Finals)

        // Check capacity feasibility
        bool regularFeasible = availableRegularSlots >= regularRequiredSlots;
        bool poolFeasible = availablePoolSlots >= poolRequiredSlots;
        bool bracketFeasible = availableBracketSlots >= bracketRequiredSlots;

        // Detect constraint conflicts
        var conflicts = DetectConflicts(
            teamCount,
            availableRegularSlots,
            regularRequiredSlots,
            minGamesPerTeam,
            maxGamesPerWeek,
            noDoubleHeaders,
            regularWeeksCount,
            guestGamesPerWeek
        );

        // Calculate recommendations
        var recommendations = CalculateRecommendations(
            teamCount,
            availableRegularSlots,
            availablePoolSlots,
            minGamesPerTeam,
            guestGamesPerWeek,
            regularWeeksCount,
            regularRequiredSlots
        );

        // Calculate capacity breakdown
        int guestSlotsReserved = regularWeeksCount * guestGamesPerWeek;
        int effectiveSlotsRemaining = availableRegularSlots - guestSlotsReserved;
        int surplusOrShortfall = availableRegularSlots - regularRequiredSlots;

        var capacity = new CapacityBreakdown(
            availableRegularSlots,
            regularRequiredSlots,
            surplusOrShortfall,
            guestSlotsReserved,
            effectiveSlotsRemaining
        );

        return new FeasibilityResult(
            regularFeasible,
            poolFeasible,
            bracketFeasible,
            conflicts,
            recommendations,
            capacity
        );
    }

    /// <summary>
    /// Calculates optimal games per team and guest games per week based on
    /// available capacity and team count.
    /// </summary>
    private static Recommendations CalculateRecommendations(
        int teamCount,
        int availableSlots,
        int availablePoolSlots,
        int currentMinGamesPerTeam,
        int currentGuestGamesPerWeek,
        int weeksCount,
        int currentRequiredSlots)
    {
        if (teamCount == 0 || availableSlots == 0)
        {
            return new Recommendations(
                0,
                0,
                0,
                "No teams or slots available for recommendations",
                "N/A"
            );
        }

        // Calculate optimal guest games first
        int optimalGuestGames = CalculateOptimalGuestGames(
            teamCount,
            availableSlots,
            currentRequiredSlots,
            weeksCount
        );

        // Calculate effective slots after accounting for guest games
        int guestSlotsReserved = weeksCount * optimalGuestGames;
        int effectiveSlots = Math.Max(0, availableSlots - guestSlotsReserved);

        // Calculate max games per team based on effective capacity
        int totalGamePairs = effectiveSlots;
        int maxGamesPerTeam = teamCount > 0 ? (totalGamePairs * 2) / teamCount : 0;

        // Calculate recommended range
        // Min: At least one full round-robin (teamCount - 1), or 2 games below max
        int minRecommended = Math.Max(
            Math.Min(teamCount - 1, maxGamesPerTeam),
            maxGamesPerTeam - 2
        );
        minRecommended = Math.Max(0, minRecommended);

        // Max: All available capacity
        int maxRecommended = maxGamesPerTeam;

        // Calculate utilization if currentMinGamesPerTeam is set
        double utilization = 0;
        if (currentMinGamesPerTeam > 0 && availableSlots > 0)
        {
            int totalCurrentRequired = currentRequiredSlots + (weeksCount * currentGuestGamesPerWeek);
            utilization = (double)totalCurrentRequired / availableSlots;
        }

        // Determine utilization status
        string utilizationStatus = utilization switch
        {
            >= 0.9 => $"High utilization ({utilization:P0})",
            >= 0.5 => $"Good utilization ({utilization:P0})",
            > 0 => $"Low utilization ({utilization:P0})",
            _ => "Not calculated"
        };

        // Build recommendation message
        string message;
        if (minRecommended == maxRecommended)
        {
            message = optimalGuestGames > 0
                ? $"Recommend {minRecommended} games per team with {optimalGuestGames} guest game(s)/week for balanced schedule"
                : $"Recommend {minRecommended} games per team based on {availableSlots} available slots";
        }
        else
        {
            message = optimalGuestGames > 0
                ? $"Recommend {minRecommended}-{maxRecommended} games per team with {optimalGuestGames} guest game(s)/week"
                : $"Recommend {minRecommended}-{maxRecommended} games per team based on {availableSlots} available slots";
        }

        return new Recommendations(
            minRecommended,
            maxRecommended,
            optimalGuestGames,
            message,
            utilizationStatus
        );
    }

    /// <summary>
    /// Calculates optimal guest games per week to balance odd team counts
    /// and fill available capacity.
    /// </summary>
    private static int CalculateOptimalGuestGames(
        int teamCount,
        int availableSlots,
        int requiredSlots,
        int weeksCount)
    {
        if (teamCount == 0 || weeksCount == 0 || availableSlots == 0)
        {
            return 0;
        }

        bool oddTeamCount = teamCount % 2 == 1;

        // For odd team counts, guest games help balance schedules
        if (oddTeamCount && requiredSlots > 0)
        {
            // Calculate if we need guest games to balance
            int totalGamesNeeded = teamCount * (requiredSlots * 2 / teamCount);
            int remainder = (requiredSlots * 2) % teamCount;

            if (remainder != 0)
            {
                // Need guest games to make it even
                int shortfall = teamCount - remainder;
                int optimalGuestGames = (int)Math.Ceiling((double)shortfall / weeksCount);

                // Cap at available capacity
                int maxPossibleGuestGames = Math.Max(0, (availableSlots - requiredSlots) / weeksCount);
                return Math.Min(optimalGuestGames, maxPossibleGuestGames);
            }
        }

        // For even team counts, or when no balance issue exists, calculate based on available capacity
        int surplusSlots = Math.Max(0, availableSlots - requiredSlots);

        // If we have significant surplus capacity (>20%), suggest 1 guest game/week
        if (surplusSlots > availableSlots * 0.2 && weeksCount > 0)
        {
            int suggestedGuestGames = Math.Min(1, surplusSlots / weeksCount);
            return suggestedGuestGames;
        }

        return 0;
    }

    /// <summary>
    /// Detects constraint conflicts that would make scheduling impossible or suboptimal.
    /// </summary>
    private static List<ConstraintConflict> DetectConflicts(
        int teamCount,
        int availableSlots,
        int requiredSlots,
        int minGamesPerTeam,
        int maxGamesPerWeek,
        bool noDoubleHeaders,
        int weeksCount,
        int guestGamesPerWeek)
    {
        var conflicts = new List<ConstraintConflict>();

        if (teamCount == 0)
        {
            return conflicts; // No conflicts if no teams
        }

        // Conflict: Insufficient capacity
        if (requiredSlots > availableSlots)
        {
            conflicts.Add(new ConstraintConflict(
                "capacity-insufficient",
                $"Impossible: {minGamesPerTeam} games/team requires {requiredSlots} slots, but only {availableSlots} available. " +
                $"Recommend {(availableSlots * 2 / teamCount)} games per team.",
                "error"
            ));
        }

        // Conflict: Guest games consuming too much capacity
        if (guestGamesPerWeek > 0)
        {
            int guestSlotsConsumed = weeksCount * guestGamesPerWeek;
            int effectiveSlots = availableSlots - guestSlotsConsumed;

            if (effectiveSlots < requiredSlots)
            {
                conflicts.Add(new ConstraintConflict(
                    "guest-games-over-consuming",
                    $"Warning: {guestGamesPerWeek} guest game(s)/week consumes {guestSlotsConsumed} slots, " +
                    $"leaving {effectiveSlots} for regular games. Need {requiredSlots} slots. " +
                    $"Reduce guest games to {Math.Max(0, (availableSlots - requiredSlots) / weeksCount)}/week.",
                    "warning"
                ));
            }
        }

        // Conflict: MaxGamesPerWeek + NoDoubleHeaders blocking schedule
        if (maxGamesPerWeek > 0 && noDoubleHeaders && weeksCount > 0)
        {
            // With no doubleheaders, each team can play at most maxGamesPerWeek games per week
            int maxPossibleGamesPerTeam = weeksCount * maxGamesPerWeek;

            if (minGamesPerTeam > maxPossibleGamesPerTeam)
            {
                conflicts.Add(new ConstraintConflict(
                    "no-doubleheaders-blocking",
                    $"Impossible: {minGamesPerTeam} games/team requires {minGamesPerTeam} weeks with max {maxGamesPerWeek} game(s)/week and no doubleheaders, " +
                    $"but season only has {weeksCount} weeks. " +
                    $"Either increase maxGamesPerWeek to {(int)Math.Ceiling((double)minGamesPerTeam / weeksCount)} or reduce games to {maxPossibleGamesPerTeam}.",
                    "error"
                ));
            }
        }

        // Conflict: MaxGamesPerWeek insufficient (without noDoubleHeaders consideration)
        if (maxGamesPerWeek > 0 && weeksCount > 0)
        {
            // Total games needed across all teams per week
            int totalGamesNeeded = requiredSlots; // Total game slots needed
            int totalWeeklyCapacity = weeksCount * maxGamesPerWeek * teamCount;
            int totalGamesForAllTeams = teamCount * minGamesPerTeam;

            if (totalGamesForAllTeams > totalWeeklyCapacity)
            {
                int requiredMaxGamesPerWeek = (int)Math.Ceiling((double)totalGamesForAllTeams / (weeksCount * teamCount));
                conflicts.Add(new ConstraintConflict(
                    "max-games-per-week-insufficient",
                    $"Warning: MaxGamesPerWeek={maxGamesPerWeek} may be insufficient for {minGamesPerTeam} games/team over {weeksCount} weeks. " +
                    $"Consider increasing to {requiredMaxGamesPerWeek} games/week.",
                    "warning"
                ));
            }
        }

        return conflicts;
    }
}

/// <summary>
/// Result of feasibility analysis containing capacity checks, conflicts, and recommendations.
/// </summary>
public record FeasibilityResult(
    bool RegularSeasonFeasible,
    bool PoolPlayFeasible,
    bool BracketFeasible,
    List<ConstraintConflict> Conflicts,
    Recommendations Recommendations,
    CapacityBreakdown Capacity
);

/// <summary>
/// Represents a constraint conflict that makes scheduling impossible or suboptimal.
/// </summary>
public record ConstraintConflict(
    string ConflictId,
    string Message,
    string Severity // "error", "warning", "info"
);

/// <summary>
/// Intelligent recommendations for games per team and guest games.
/// </summary>
public record Recommendations(
    int MinGamesPerTeam,
    int MaxGamesPerTeam,
    int OptimalGuestGamesPerWeek,
    string Message,
    string UtilizationStatus
);

/// <summary>
/// Detailed breakdown of slot capacity and utilization.
/// </summary>
public record CapacityBreakdown(
    int AvailableRegularSlots,
    int RequiredRegularSlots,
    int SurplusOrShortfall,
    int GuestSlotsReserved,
    int EffectiveSlotsRemaining
);
