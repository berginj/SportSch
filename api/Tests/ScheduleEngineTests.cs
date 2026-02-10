using GameSwap.Functions.Scheduling;
using Xunit;

namespace GameSwap.Functions.Tests;

public class ScheduleEngineTests
{
    [Fact]
    public void AssignMatchups_DistributesLimitedSlotsAcrossTeams()
    {
        var slots = new List<ScheduleSlot>
        {
            new("slot-1", "2026-03-01", "18:00", "20:00", "park/field", ""),
            new("slot-2", "2026-03-08", "18:00", "20:00", "park/field", ""),
            new("slot-3", "2026-03-15", "18:00", "20:00", "park/field", "")
        };

        var teams = new List<string> { "A", "B", "C", "D" };
        var matchups = new List<MatchupPair>
        {
            new("A", "B"),
            new("A", "C"),
            new("A", "D"),
            new("B", "C"),
            new("B", "D"),
            new("C", "D")
        };

        var constraints = new ScheduleConstraints(
            MaxGamesPerWeek: null,
            NoDoubleHeaders: false,
            BalanceHomeAway: false,
            ExternalOfferPerWeek: 0);

        var result = ScheduleEngine.AssignMatchups(slots, matchups, teams, constraints);

        var gamesByTeam = teams.ToDictionary(t => t, _ => 0, StringComparer.OrdinalIgnoreCase);
        foreach (var assignment in result.Assignments)
        {
            if (!string.IsNullOrWhiteSpace(assignment.HomeTeamId))
                gamesByTeam[assignment.HomeTeamId] += 1;
            if (!string.IsNullOrWhiteSpace(assignment.AwayTeamId))
                gamesByTeam[assignment.AwayTeamId] += 1;
        }

        var spread = gamesByTeam.Values.Max() - gamesByTeam.Values.Min();
        Assert.True(spread <= 1, $"Expected assigned-game spread <= 1, but was {spread}.");
    }
}
