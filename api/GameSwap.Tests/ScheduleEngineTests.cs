using GameSwap.Functions.Scheduling;
using Xunit;

namespace GameSwap.Tests;

public class ScheduleEngineTests
{
    [Fact]
    public void BuildRoundRobin_ForFourTeams_CreatesSixMatchups()
    {
        var teams = new List<string> { "A", "B", "C", "D" };
        var matchups = ScheduleEngine.BuildRoundRobin(teams);

        Assert.Equal(6, matchups.Count);
        var pairs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in matchups)
        {
            var ordered = string.CompareOrdinal(m.HomeTeamId, m.AwayTeamId) < 0
                ? $"{m.HomeTeamId}|{m.AwayTeamId}"
                : $"{m.AwayTeamId}|{m.HomeTeamId}";
            pairs.Add(ordered);
        }

        Assert.Equal(6, pairs.Count);
    }
}
