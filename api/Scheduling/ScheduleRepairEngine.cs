using System.Globalization;

namespace GameSwap.Functions.Scheduling;

public record ScheduleDiffChange(
    string ChangeType,
    string? GameId,
    string? FromSlotId,
    string? ToSlotId,
    object? Before,
    object? After);

public record ScheduleRepairProposal(
    string ProposalId,
    string Title,
    string Rationale,
    IReadOnlyList<string> FixesRuleIds,
    IReadOnlyList<ScheduleDiffChange> Changes,
    int GamesMoved,
    int TeamsTouched,
    int WeeksTouched,
    int HardViolationsResolved,
    int HardViolationsRemaining,
    double SoftScoreDelta,
    double UtilizationDelta,
    bool RequiresUserAction,
    Dictionary<string, object?> BeforeAfterSummary);

public static class ScheduleRepairEngine
{
    public static IReadOnlyList<ScheduleRepairProposal> Propose(
        ScheduleResult result,
        ScheduleRuleHealthReport ruleHealth,
        ScheduleValidationV2Config validationConfig,
        IReadOnlyList<string> teams,
        int maxProposals = 10)
    {
        var proposals = new List<ScheduleRepairProposal>();
        var hardGroups = (ruleHealth.Groups ?? Array.Empty<ScheduleRuleGroupReport>())
            .Where(g => string.Equals(g.Severity, "hard", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (hardGroups.Count == 0)
            return proposals;

        var assignmentsBySlot = result.Assignments
            .Where(a => !string.IsNullOrWhiteSpace(a.SlotId))
            .GroupBy(a => a.SlotId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var unusedSlots = result.UnassignedSlots
            .Where(s => !string.IsNullOrWhiteSpace(s.SlotId))
            .ToList();

        foreach (var group in hardGroups.Take(3))
        {
            proposals.AddRange(GenerateMoveProposalsForGroup(result, ruleHealth, group, validationConfig, teams, assignmentsBySlot, unusedSlots));
            proposals.AddRange(GenerateSuggestionProposalsForGroup(result, ruleHealth, group, validationConfig, unusedSlots));
        }

        return proposals
            .GroupBy(p => p.ProposalId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderByDescending(p => p.HardViolationsResolved)
            .ThenBy(p => p.HardViolationsRemaining)
            .ThenBy(p => p.GamesMoved)
            .ThenBy(p => p.TeamsTouched)
            .ThenBy(p => p.WeeksTouched)
            .ThenByDescending(p => p.SoftScoreDelta)
            .ThenByDescending(p => p.UtilizationDelta)
            .ThenBy(p => p.Title, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, maxProposals))
            .ToList();
    }

    private static IEnumerable<ScheduleRepairProposal> GenerateMoveProposalsForGroup(
        ScheduleResult result,
        ScheduleRuleHealthReport baseline,
        ScheduleRuleGroupReport group,
        ScheduleValidationV2Config validationConfig,
        IReadOnlyList<string> teams,
        IReadOnlyDictionary<string, ScheduleAssignment> assignmentsBySlot,
        IReadOnlyList<ScheduleAssignment> unusedSlots)
    {
        if (unusedSlots.Count == 0) yield break;

        var sourceAssignments = group.Violations
            .SelectMany(v => v.SlotIds)
            .Where(slotId => !string.IsNullOrWhiteSpace(slotId) && assignmentsBySlot.ContainsKey(slotId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(slotId => assignmentsBySlot[slotId])
            .Take(4)
            .ToList();

        foreach (var assignment in sourceAssignments)
        {
            var candidates = RankUnusedSlotsForMove(assignment, unusedSlots, validationConfig)
                .Take(12)
                .ToList();
            foreach (var target in candidates)
            {
                var proposal = TryBuildMoveProposal(result, baseline, group, validationConfig, teams, assignment, target);
                if (proposal is not null)
                    yield return proposal;
            }
        }
    }

    private static ScheduleRepairProposal? TryBuildMoveProposal(
        ScheduleResult result,
        ScheduleRuleHealthReport baseline,
        ScheduleRuleGroupReport targetGroup,
        ScheduleValidationV2Config validationConfig,
        IReadOnlyList<string> teams,
        ScheduleAssignment assignment,
        ScheduleAssignment targetSlot)
    {
        if (string.Equals(assignment.SlotId, targetSlot.SlotId, StringComparison.OrdinalIgnoreCase))
            return null;

        var movedAssignments = new List<ScheduleAssignment>(result.Assignments.Count);
        foreach (var a in result.Assignments)
        {
            if (!string.Equals(a.SlotId, assignment.SlotId, StringComparison.OrdinalIgnoreCase))
            {
                movedAssignments.Add(a);
                continue;
            }

            movedAssignments.Add(a with
            {
                SlotId = targetSlot.SlotId,
                GameDate = targetSlot.GameDate,
                StartTime = targetSlot.StartTime,
                EndTime = targetSlot.EndTime,
                FieldKey = targetSlot.FieldKey
            });
        }

        var movedUnassignedSlots = result.UnassignedSlots
            .Where(s => !string.Equals(s.SlotId, targetSlot.SlotId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        movedUnassignedSlots.Add(assignment with
        {
            HomeTeamId = "",
            AwayTeamId = "",
            IsExternalOffer = false
        });

        var movedResult = new ScheduleResult(
            result.Summary,
            movedAssignments,
            movedUnassignedSlots,
            new List<MatchupPair>(result.UnassignedMatchups));
        var after = ScheduleValidationV2.Validate(movedResult, validationConfig, teams).RuleHealth;
        if (after.HardViolationCount >= baseline.HardViolationCount) return null;

        var beforeGroupCount = targetGroup.Count;
        var afterGroup = after.Groups.FirstOrDefault(g =>
            string.Equals(g.RuleId, targetGroup.RuleId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(g.Severity, targetGroup.Severity, StringComparison.OrdinalIgnoreCase));
        var afterGroupCount = afterGroup?.Count ?? 0;
        if (afterGroupCount >= beforeGroupCount && after.HardViolationCount >= baseline.HardViolationCount)
            return null;

        var beforeWeek = WeekKey(assignment.GameDate);
        var afterWeek = WeekKey(targetSlot.GameDate);
        var teamsTouched = TeamCount(assignment);
        var weeksTouched = string.Equals(beforeWeek, afterWeek, StringComparison.OrdinalIgnoreCase) ? 1 : 2;
        var hardResolved = Math.Max(0, baseline.HardViolationCount - after.HardViolationCount);
        var proposalId = $"move:{targetGroup.RuleId}:{assignment.SlotId}->{targetSlot.SlotId}";
        var gameId = BuildGameId(assignment);

        return new ScheduleRepairProposal(
            ProposalId: proposalId,
            Title: $"Move {FormatGameLabel(assignment)} to {targetSlot.GameDate} {targetSlot.StartTime}-{targetSlot.EndTime} ({targetSlot.FieldKey})",
            Rationale: $"Moves one game into an unused slot to reduce hard rule violations ({targetGroup.RuleId}) with minimal schedule churn.",
            FixesRuleIds: new[] { targetGroup.RuleId },
            Changes: new[]
            {
                new ScheduleDiffChange(
                    ChangeType: "move",
                    GameId: gameId,
                    FromSlotId: assignment.SlotId,
                    ToSlotId: targetSlot.SlotId,
                    Before: new { slotId = assignment.SlotId, gameDate = assignment.GameDate, startTime = assignment.StartTime, endTime = assignment.EndTime, fieldKey = assignment.FieldKey, homeTeamId = assignment.HomeTeamId, awayTeamId = assignment.AwayTeamId },
                    After: new { slotId = targetSlot.SlotId, gameDate = targetSlot.GameDate, startTime = targetSlot.StartTime, endTime = targetSlot.EndTime, fieldKey = targetSlot.FieldKey, homeTeamId = assignment.HomeTeamId, awayTeamId = assignment.AwayTeamId })
            },
            GamesMoved: 1,
            TeamsTouched: teamsTouched,
            WeeksTouched: weeksTouched,
            HardViolationsResolved: hardResolved,
            HardViolationsRemaining: after.HardViolationCount,
            SoftScoreDelta: after.SoftScore - baseline.SoftScore,
            UtilizationDelta: 0,
            RequiresUserAction: false,
            BeforeAfterSummary: new Dictionary<string, object?>
            {
                ["beforeHardViolations"] = baseline.HardViolationCount,
                ["afterHardViolations"] = after.HardViolationCount,
                ["beforeSoftScore"] = baseline.SoftScore,
                ["afterSoftScore"] = after.SoftScore,
                ["targetRuleBefore"] = beforeGroupCount,
                ["targetRuleAfter"] = afterGroupCount
            });
    }

    private static IEnumerable<ScheduleRepairProposal> GenerateSuggestionProposalsForGroup(
        ScheduleResult result,
        ScheduleRuleHealthReport baseline,
        ScheduleRuleGroupReport group,
        ScheduleValidationV2Config validationConfig,
        IReadOnlyList<ScheduleAssignment> unusedSlots)
    {
        if (string.Equals(group.RuleId, "unscheduled-required-matchups", StringComparison.OrdinalIgnoreCase))
        {
            if (unusedSlots.Count == 0)
            {
                yield return new ScheduleRepairProposal(
                    ProposalId: $"add-slot:{group.RuleId}",
                    Title: "Add at least one game-capable slot in the most constrained week(s)",
                    Rationale: "Required matchups remain unscheduled and no unused game-capable slots are available. Capacity must be added or reclassified.",
                    FixesRuleIds: new[] { group.RuleId },
                    Changes: new[]
                    {
                        new ScheduleDiffChange(
                            "add-slot-suggestion",
                            null,
                            null,
                            null,
                            new { unusedGameCapableSlots = 0, unassignedRequiredMatchups = group.Count },
                            new { suggestedAction = "Add or reclassify one 2-hour game-capable slot in a regular-season week with team shortfalls." })
                    },
                    GamesMoved: 0,
                    TeamsTouched: Convert.ToInt32(group.SmallestAffectedSet.TryGetValue("teamCount", out var t) ? t : 0),
                    WeeksTouched: Convert.ToInt32(group.SmallestAffectedSet.TryGetValue("weekCount", out var w) ? w : 0),
                    HardViolationsResolved: 0,
                    HardViolationsRemaining: baseline.HardViolationCount,
                    SoftScoreDelta: 0,
                    UtilizationDelta: 1,
                    RequiresUserAction: true,
                    BeforeAfterSummary: new Dictionary<string, object?>
                    {
                        ["beforeHardViolations"] = baseline.HardViolationCount,
                        ["afterHardViolations"] = "unknown-until-capacity-added"
                    });
            }

            if (validationConfig.NoDoubleHeaders)
            {
                yield return new ScheduleRepairProposal(
                    ProposalId: $"relax-no-doubleheaders:{group.RuleId}",
                    Title: "Relax no-doubleheaders (manual action)",
                    Rationale: "Allowing a limited number of doubleheaders can unlock placement of remaining required matchups when slot capacity is tight.",
                    FixesRuleIds: new[] { group.RuleId, "double-header" },
                    Changes: new[]
                    {
                        new ScheduleDiffChange(
                            "relax-rule-suggestion",
                            null,
                            null,
                            null,
                            new { noDoubleHeaders = true },
                            new { noDoubleHeaders = false })
                    },
                    GamesMoved: 0,
                    TeamsTouched: Convert.ToInt32(group.SmallestAffectedSet.TryGetValue("teamCount", out var t) ? t : 0),
                    WeeksTouched: Convert.ToInt32(group.SmallestAffectedSet.TryGetValue("weekCount", out var w) ? w : 0),
                    HardViolationsResolved: 0,
                    HardViolationsRemaining: baseline.HardViolationCount,
                    SoftScoreDelta: 0,
                    UtilizationDelta: Math.Min(1, group.Count),
                    RequiresUserAction: true,
                    BeforeAfterSummary: new Dictionary<string, object?>
                    {
                        ["beforeHardViolations"] = baseline.HardViolationCount,
                        ["rule"] = "noDoubleHeaders"
                    });
            }

            if (validationConfig.MaxGamesPerWeek.HasValue && validationConfig.MaxGamesPerWeek.Value > 0)
            {
                yield return new ScheduleRepairProposal(
                    ProposalId: $"increase-max-games-week:{group.RuleId}",
                    Title: $"Increase max games/week to {validationConfig.MaxGamesPerWeek.Value + 1} (manual action)",
                    Rationale: "Raising the weekly cap by one may allow the scheduler to place matchups instead of leaving teams with zero-game weeks.",
                    FixesRuleIds: new[] { group.RuleId, "max-games-per-week" },
                    Changes: new[]
                    {
                        new ScheduleDiffChange(
                            "relax-rule-suggestion",
                            null,
                            null,
                            null,
                            new { maxGamesPerWeek = validationConfig.MaxGamesPerWeek.Value },
                            new { maxGamesPerWeek = validationConfig.MaxGamesPerWeek.Value + 1 })
                    },
                    GamesMoved: 0,
                    TeamsTouched: Convert.ToInt32(group.SmallestAffectedSet.TryGetValue("teamCount", out var t2) ? t2 : 0),
                    WeeksTouched: Convert.ToInt32(group.SmallestAffectedSet.TryGetValue("weekCount", out var w2) ? w2 : 0),
                    HardViolationsResolved: 0,
                    HardViolationsRemaining: baseline.HardViolationCount,
                    SoftScoreDelta: 0,
                    UtilizationDelta: Math.Min(1, group.Count),
                    RequiresUserAction: true,
                    BeforeAfterSummary: new Dictionary<string, object?>
                    {
                        ["beforeHardViolations"] = baseline.HardViolationCount,
                        ["rule"] = "maxGamesPerWeek"
                    });
            }
        }
        else if (string.Equals(group.RuleId, "double-header", StringComparison.OrdinalIgnoreCase) && validationConfig.NoDoubleHeaders)
        {
            yield return new ScheduleRepairProposal(
                ProposalId: "relax-no-doubleheaders:double-header",
                Title: "Allow doubleheaders (manual action)",
                Rationale: "Doubleheader rule is currently hard. Temporarily relaxing it will unblock apply, but should be an explicit commissioner decision.",
                FixesRuleIds: new[] { "double-header" },
                Changes: new[]
                {
                    new ScheduleDiffChange("relax-rule-suggestion", null, null, null, new { noDoubleHeaders = true }, new { noDoubleHeaders = false })
                },
                GamesMoved: 0,
                TeamsTouched: Convert.ToInt32(group.SmallestAffectedSet.TryGetValue("teamCount", out var t) ? t : 0),
                WeeksTouched: Convert.ToInt32(group.SmallestAffectedSet.TryGetValue("weekCount", out var w) ? w : 0),
                HardViolationsResolved: 0,
                HardViolationsRemaining: baseline.HardViolationCount,
                SoftScoreDelta: 0,
                UtilizationDelta: 0,
                RequiresUserAction: true,
                BeforeAfterSummary: new Dictionary<string, object?> { ["beforeHardViolations"] = baseline.HardViolationCount });
        }
        else if (string.Equals(group.RuleId, "max-games-per-week", StringComparison.OrdinalIgnoreCase) && validationConfig.MaxGamesPerWeek.HasValue)
        {
            var current = validationConfig.MaxGamesPerWeek.Value;
            yield return new ScheduleRepairProposal(
                ProposalId: "increase-max-games-week:max-games-per-week",
                Title: $"Increase max games/week to {current + 1} (manual action)",
                Rationale: "The weekly game cap is the blocking hard rule. Increasing it by one may resolve the affected weeks.",
                FixesRuleIds: new[] { "max-games-per-week" },
                Changes: new[]
                {
                    new ScheduleDiffChange("relax-rule-suggestion", null, null, null, new { maxGamesPerWeek = current }, new { maxGamesPerWeek = current + 1 })
                },
                GamesMoved: 0,
                TeamsTouched: Convert.ToInt32(group.SmallestAffectedSet.TryGetValue("teamCount", out var t) ? t : 0),
                WeeksTouched: Convert.ToInt32(group.SmallestAffectedSet.TryGetValue("weekCount", out var w) ? w : 0),
                HardViolationsResolved: 0,
                HardViolationsRemaining: baseline.HardViolationCount,
                SoftScoreDelta: 0,
                UtilizationDelta: 0,
                RequiresUserAction: true,
                BeforeAfterSummary: new Dictionary<string, object?> { ["beforeHardViolations"] = baseline.HardViolationCount });
        }
    }

    private static IEnumerable<ScheduleAssignment> RankUnusedSlotsForMove(
        ScheduleAssignment source,
        IReadOnlyList<ScheduleAssignment> unusedSlots,
        ScheduleValidationV2Config validationConfig)
    {
        var sourceDate = ParseDate(source.GameDate);
        var sourceStart = ParseMinutes(source.StartTime);
        return unusedSlots
            .Where(s => !string.IsNullOrWhiteSpace(s.SlotId))
            .Where(s => !IsBlackout(s.GameDate, validationConfig.BlackoutWindows))
            .OrderBy(s => DateDistanceDays(sourceDate, ParseDate(s.GameDate)))
            .ThenBy(s => string.Equals(source.GameDate, s.GameDate, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(s => sourceStart.HasValue && ParseMinutes(s.StartTime).HasValue ? Math.Abs(sourceStart.Value - ParseMinutes(s.StartTime)!.Value) : int.MaxValue)
            .ThenBy(s => string.Equals(source.FieldKey, s.FieldKey, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(s => s.GameDate, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.StartTime, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.FieldKey, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsBlackout(string gameDate, IReadOnlyList<ScheduleBlackoutWindow>? blackouts)
    {
        if (blackouts is null || blackouts.Count == 0) return false;
        if (!DateOnly.TryParseExact(gameDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return false;
        foreach (var b in blackouts)
        {
            if (date >= b.StartDate && date <= b.EndDate) return true;
        }
        return false;
    }

    private static DateOnly? ParseDate(string gameDate)
        => DateOnly.TryParseExact(gameDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    private static int? ParseMinutes(string hhmm)
    {
        if (string.IsNullOrWhiteSpace(hhmm)) return null;
        return TimeOnly.TryParseExact(hhmm, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)
            ? (t.Hour * 60) + t.Minute
            : null;
    }

    private static int DateDistanceDays(DateOnly? a, DateOnly? b)
    {
        if (!a.HasValue || !b.HasValue) return int.MaxValue;
        return Math.Abs(a.Value.DayNumber - b.Value.DayNumber);
    }

    private static int TeamCount(ScheduleAssignment a)
    {
        var count = 0;
        if (!string.IsNullOrWhiteSpace(a.HomeTeamId)) count++;
        if (!string.IsNullOrWhiteSpace(a.AwayTeamId)) count++;
        return count;
    }

    private static string BuildGameId(ScheduleAssignment a)
        => $"{a.HomeTeamId}|{a.AwayTeamId}|{a.GameDate}|{a.StartTime}|{a.SlotId}";

    private static string FormatGameLabel(ScheduleAssignment a)
    {
        if (a.IsExternalOffer)
            return $"{a.HomeTeamId} external offer";
        return $"{a.HomeTeamId} vs {a.AwayTeamId}";
    }

    private static string WeekKey(string gameDate)
    {
        if (!DateTime.TryParseExact(gameDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return "";
        var cal = CultureInfo.InvariantCulture.Calendar;
        var week = cal.GetWeekOfYear(dt, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
        return $"{dt.Year}-W{week:D2}";
    }
}
