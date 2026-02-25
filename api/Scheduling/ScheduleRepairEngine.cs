using System.Globalization;
using System.Text.Json;

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
            proposals.AddRange(GenerateSwapProposalsForGroup(result, ruleHealth, group, validationConfig, teams));
            proposals.AddRange(GenerateRotationProposalsForGroup(result, ruleHealth, group, validationConfig, teams));
            proposals.AddRange(GenerateSuggestionProposalsForGroup(result, ruleHealth, group, validationConfig, unusedSlots));
        }

        var annotated = proposals
            .Select(p => AnnotatePriorityPairImpact(p, validationConfig.MatchupPriorityByPair))
            .ToList();

        return annotated
            .GroupBy(p => p.ProposalId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderByDescending(p => p.HardViolationsResolved)
            .ThenBy(p => p.HardViolationsRemaining)
            .ThenBy(p => p.GamesMoved)
            .ThenBy(p => p.TeamsTouched)
            .ThenBy(p => p.WeeksTouched)
            // Hard-fix / minimal-change ordering stays authoritative.
            // When comparable, prefer proposals that move priority matchups later (not earlier).
            .ThenByDescending(GetPriorityImpactLaterCount)
            .ThenBy(GetPriorityImpactEarlierCount)
            .ThenByDescending(GetPriorityImpactWeightDelta)
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

        var sourceAssignments = GetSourceAssignmentsForGroup(result.Assignments, group, assignmentsBySlot)
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

    private static IEnumerable<ScheduleRepairProposal> GenerateSwapProposalsForGroup(
        ScheduleResult result,
        ScheduleRuleHealthReport baseline,
        ScheduleRuleGroupReport group,
        ScheduleValidationV2Config validationConfig,
        IReadOnlyList<string> teams)
    {
        var sourceAssignments = GetSourceAssignmentsForGroup(
                result.Assignments,
                group,
                result.Assignments
                    .Where(a => !string.IsNullOrWhiteSpace(a.SlotId))
                    .GroupBy(a => a.SlotId, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase))
            .Take(4)
            .ToList();
        if (sourceAssignments.Count == 0) yield break;

        var allAssignments = result.Assignments
            .Where(a => !string.IsNullOrWhiteSpace(a.SlotId))
            .OrderBy(a => a.GameDate, StringComparer.OrdinalIgnoreCase)
            .ThenBy(a => a.StartTime, StringComparer.OrdinalIgnoreCase)
            .ThenBy(a => a.FieldKey, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var source in sourceAssignments)
        {
            var candidates = RankSwapCandidates(source, allAssignments, validationConfig)
                .Take(16)
                .ToList();
            foreach (var candidate in candidates)
            {
                var proposal = TryBuildSwapProposal(result, baseline, group, validationConfig, teams, source, candidate);
                if (proposal is not null)
                    yield return proposal;
            }
        }
    }

    private static IEnumerable<ScheduleRepairProposal> GenerateRotationProposalsForGroup(
        ScheduleResult result,
        ScheduleRuleHealthReport baseline,
        ScheduleRuleGroupReport group,
        ScheduleValidationV2Config validationConfig,
        IReadOnlyList<string> teams)
    {
        var assignmentsBySlot = result.Assignments
            .Where(a => !string.IsNullOrWhiteSpace(a.SlotId))
            .GroupBy(a => a.SlotId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var sourceAssignments = GetSourceAssignmentsForGroup(result.Assignments, group, assignmentsBySlot)
            .Take(3)
            .ToList();
        if (sourceAssignments.Count == 0) yield break;

        var allAssignments = result.Assignments
            .Where(a => !string.IsNullOrWhiteSpace(a.SlotId))
            .OrderBy(a => a.GameDate, StringComparer.OrdinalIgnoreCase)
            .ThenBy(a => a.StartTime, StringComparer.OrdinalIgnoreCase)
            .ThenBy(a => a.FieldKey, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var first in sourceAssignments)
        {
            var secondCandidates = RankSwapCandidates(first, allAssignments, validationConfig)
                .Where(a => !string.Equals(a.SlotId, first.SlotId, StringComparison.OrdinalIgnoreCase))
                .Take(10)
                .ToList();

            foreach (var second in secondCandidates)
            {
                var thirdCandidates = RankSwapCandidates(second, allAssignments, validationConfig)
                    .Where(a => !string.Equals(a.SlotId, first.SlotId, StringComparison.OrdinalIgnoreCase))
                    .Where(a => !string.Equals(a.SlotId, second.SlotId, StringComparison.OrdinalIgnoreCase))
                    .Take(10)
                    .ToList();

                foreach (var third in thirdCandidates)
                {
                    var proposal = TryBuildThreeGameRotationProposal(result, baseline, group, validationConfig, teams, first, second, third);
                    if (proposal is not null)
                        yield return proposal;
                }
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

    private static ScheduleRepairProposal? TryBuildSwapProposal(
        ScheduleResult result,
        ScheduleRuleHealthReport baseline,
        ScheduleRuleGroupReport targetGroup,
        ScheduleValidationV2Config validationConfig,
        IReadOnlyList<string> teams,
        ScheduleAssignment first,
        ScheduleAssignment second)
    {
        if (string.Equals(first.SlotId, second.SlotId, StringComparison.OrdinalIgnoreCase))
            return null;

        var swappedAssignments = new List<ScheduleAssignment>(result.Assignments.Count);
        foreach (var a in result.Assignments)
        {
            if (string.Equals(a.SlotId, first.SlotId, StringComparison.OrdinalIgnoreCase))
            {
                swappedAssignments.Add(a with
                {
                    SlotId = second.SlotId,
                    GameDate = second.GameDate,
                    StartTime = second.StartTime,
                    EndTime = second.EndTime,
                    FieldKey = second.FieldKey
                });
            }
            else if (string.Equals(a.SlotId, second.SlotId, StringComparison.OrdinalIgnoreCase))
            {
                swappedAssignments.Add(a with
                {
                    SlotId = first.SlotId,
                    GameDate = first.GameDate,
                    StartTime = first.StartTime,
                    EndTime = first.EndTime,
                    FieldKey = first.FieldKey
                });
            }
            else
            {
                swappedAssignments.Add(a);
            }
        }

        var swappedResult = new ScheduleResult(
            result.Summary,
            swappedAssignments,
            new List<ScheduleAssignment>(result.UnassignedSlots),
            new List<MatchupPair>(result.UnassignedMatchups));
        var after = ScheduleValidationV2.Validate(swappedResult, validationConfig, teams).RuleHealth;
        if (after.HardViolationCount >= baseline.HardViolationCount) return null;

        var beforeGroupCount = targetGroup.Count;
        var afterGroup = after.Groups.FirstOrDefault(g =>
            string.Equals(g.RuleId, targetGroup.RuleId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(g.Severity, targetGroup.Severity, StringComparison.OrdinalIgnoreCase));
        var afterGroupCount = afterGroup?.Count ?? 0;
        if (afterGroupCount >= beforeGroupCount && after.HardViolationCount >= baseline.HardViolationCount)
            return null;

        var hardResolved = Math.Max(0, baseline.HardViolationCount - after.HardViolationCount);
        var teamIdsTouched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddTeams(teamIdsTouched, first);
        AddTeams(teamIdsTouched, second);
        var weekKeysTouched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddWeek(weekKeysTouched, first.GameDate);
        AddWeek(weekKeysTouched, second.GameDate);

        var proposalId = $"swap:{targetGroup.RuleId}:{first.SlotId}<->{second.SlotId}";
        return new ScheduleRepairProposal(
            ProposalId: proposalId,
            Title: $"Swap {FormatGameLabel(first)} with {FormatGameLabel(second)}",
            Rationale: $"Swaps two games to reduce hard rule violations ({targetGroup.RuleId}) without consuming extra capacity.",
            FixesRuleIds: new[] { targetGroup.RuleId },
            Changes: new[]
            {
                new ScheduleDiffChange(
                    ChangeType: "move",
                    GameId: BuildGameId(first),
                    FromSlotId: first.SlotId,
                    ToSlotId: second.SlotId,
                    Before: new { slotId = first.SlotId, gameDate = first.GameDate, startTime = first.StartTime, endTime = first.EndTime, fieldKey = first.FieldKey, homeTeamId = first.HomeTeamId, awayTeamId = first.AwayTeamId },
                    After: new { slotId = second.SlotId, gameDate = second.GameDate, startTime = second.StartTime, endTime = second.EndTime, fieldKey = second.FieldKey, homeTeamId = first.HomeTeamId, awayTeamId = first.AwayTeamId }),
                new ScheduleDiffChange(
                    ChangeType: "move",
                    GameId: BuildGameId(second),
                    FromSlotId: second.SlotId,
                    ToSlotId: first.SlotId,
                    Before: new { slotId = second.SlotId, gameDate = second.GameDate, startTime = second.StartTime, endTime = second.EndTime, fieldKey = second.FieldKey, homeTeamId = second.HomeTeamId, awayTeamId = second.AwayTeamId },
                    After: new { slotId = first.SlotId, gameDate = first.GameDate, startTime = first.StartTime, endTime = first.EndTime, fieldKey = first.FieldKey, homeTeamId = second.HomeTeamId, awayTeamId = second.AwayTeamId })
            },
            GamesMoved: 2,
            TeamsTouched: teamIdsTouched.Count,
            WeeksTouched: weekKeysTouched.Count,
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

    private static ScheduleRepairProposal? TryBuildThreeGameRotationProposal(
        ScheduleResult result,
        ScheduleRuleHealthReport baseline,
        ScheduleRuleGroupReport targetGroup,
        ScheduleValidationV2Config validationConfig,
        IReadOnlyList<string> teams,
        ScheduleAssignment first,
        ScheduleAssignment second,
        ScheduleAssignment third)
    {
        if (string.Equals(first.SlotId, second.SlotId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(first.SlotId, third.SlotId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(second.SlotId, third.SlotId, StringComparison.OrdinalIgnoreCase))
            return null;

        var rotatedAssignments = new List<ScheduleAssignment>(result.Assignments.Count);
        foreach (var a in result.Assignments)
        {
            if (string.Equals(a.SlotId, first.SlotId, StringComparison.OrdinalIgnoreCase))
            {
                rotatedAssignments.Add(a with
                {
                    SlotId = second.SlotId,
                    GameDate = second.GameDate,
                    StartTime = second.StartTime,
                    EndTime = second.EndTime,
                    FieldKey = second.FieldKey
                });
            }
            else if (string.Equals(a.SlotId, second.SlotId, StringComparison.OrdinalIgnoreCase))
            {
                rotatedAssignments.Add(a with
                {
                    SlotId = third.SlotId,
                    GameDate = third.GameDate,
                    StartTime = third.StartTime,
                    EndTime = third.EndTime,
                    FieldKey = third.FieldKey
                });
            }
            else if (string.Equals(a.SlotId, third.SlotId, StringComparison.OrdinalIgnoreCase))
            {
                rotatedAssignments.Add(a with
                {
                    SlotId = first.SlotId,
                    GameDate = first.GameDate,
                    StartTime = first.StartTime,
                    EndTime = first.EndTime,
                    FieldKey = first.FieldKey
                });
            }
            else
            {
                rotatedAssignments.Add(a);
            }
        }

        var rotatedResult = new ScheduleResult(
            result.Summary,
            rotatedAssignments,
            new List<ScheduleAssignment>(result.UnassignedSlots),
            new List<MatchupPair>(result.UnassignedMatchups));
        var after = ScheduleValidationV2.Validate(rotatedResult, validationConfig, teams).RuleHealth;
        if (after.HardViolationCount >= baseline.HardViolationCount) return null;

        var beforeGroupCount = targetGroup.Count;
        var afterGroup = after.Groups.FirstOrDefault(g =>
            string.Equals(g.RuleId, targetGroup.RuleId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(g.Severity, targetGroup.Severity, StringComparison.OrdinalIgnoreCase));
        var afterGroupCount = afterGroup?.Count ?? 0;
        if (afterGroupCount >= beforeGroupCount && after.HardViolationCount >= baseline.HardViolationCount)
            return null;

        var teamIdsTouched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddTeams(teamIdsTouched, first);
        AddTeams(teamIdsTouched, second);
        AddTeams(teamIdsTouched, third);
        var weekKeysTouched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddWeek(weekKeysTouched, first.GameDate);
        AddWeek(weekKeysTouched, second.GameDate);
        AddWeek(weekKeysTouched, third.GameDate);
        var hardResolved = Math.Max(0, baseline.HardViolationCount - after.HardViolationCount);

        var rotationPairs = new[]
        {
            $"{first.SlotId}->{second.SlotId}",
            $"{second.SlotId}->{third.SlotId}",
            $"{third.SlotId}->{first.SlotId}"
        };
        Array.Sort(rotationPairs, StringComparer.OrdinalIgnoreCase);
        var proposalId = $"rotate3:{targetGroup.RuleId}:{string.Join(",", rotationPairs)}";

        return new ScheduleRepairProposal(
            ProposalId: proposalId,
            Title: $"Rotate 3 games across slots to reduce {targetGroup.RuleId}",
            Rationale: $"Rotates three games in a minimal cycle to reduce hard rule violations ({targetGroup.RuleId}) when single moves or swaps are not enough.",
            FixesRuleIds: new[] { targetGroup.RuleId },
            Changes: new[]
            {
                new ScheduleDiffChange(
                    ChangeType: "move",
                    GameId: BuildGameId(first),
                    FromSlotId: first.SlotId,
                    ToSlotId: second.SlotId,
                    Before: new { slotId = first.SlotId, gameDate = first.GameDate, startTime = first.StartTime, endTime = first.EndTime, fieldKey = first.FieldKey, homeTeamId = first.HomeTeamId, awayTeamId = first.AwayTeamId },
                    After: new { slotId = second.SlotId, gameDate = second.GameDate, startTime = second.StartTime, endTime = second.EndTime, fieldKey = second.FieldKey, homeTeamId = first.HomeTeamId, awayTeamId = first.AwayTeamId }),
                new ScheduleDiffChange(
                    ChangeType: "move",
                    GameId: BuildGameId(second),
                    FromSlotId: second.SlotId,
                    ToSlotId: third.SlotId,
                    Before: new { slotId = second.SlotId, gameDate = second.GameDate, startTime = second.StartTime, endTime = second.EndTime, fieldKey = second.FieldKey, homeTeamId = second.HomeTeamId, awayTeamId = second.AwayTeamId },
                    After: new { slotId = third.SlotId, gameDate = third.GameDate, startTime = third.StartTime, endTime = third.EndTime, fieldKey = third.FieldKey, homeTeamId = second.HomeTeamId, awayTeamId = second.AwayTeamId }),
                new ScheduleDiffChange(
                    ChangeType: "move",
                    GameId: BuildGameId(third),
                    FromSlotId: third.SlotId,
                    ToSlotId: first.SlotId,
                    Before: new { slotId = third.SlotId, gameDate = third.GameDate, startTime = third.StartTime, endTime = third.EndTime, fieldKey = third.FieldKey, homeTeamId = third.HomeTeamId, awayTeamId = third.AwayTeamId },
                    After: new { slotId = first.SlotId, gameDate = first.GameDate, startTime = first.StartTime, endTime = first.EndTime, fieldKey = first.FieldKey, homeTeamId = third.HomeTeamId, awayTeamId = third.AwayTeamId })
            },
            GamesMoved: 3,
            TeamsTouched: teamIdsTouched.Count,
            WeeksTouched: weekKeysTouched.Count,
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
                ["targetRuleAfter"] = afterGroupCount,
                ["repairMoveType"] = "rotate3"
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

    private static IEnumerable<ScheduleAssignment> RankSwapCandidates(
        ScheduleAssignment source,
        IReadOnlyList<ScheduleAssignment> assignments,
        ScheduleValidationV2Config validationConfig)
    {
        var sourceDate = ParseDate(source.GameDate);
        var sourceStart = ParseMinutes(source.StartTime);
        return assignments
            .Where(a => !string.Equals(a.SlotId, source.SlotId, StringComparison.OrdinalIgnoreCase))
            .Where(a => !IsBlackout(source.GameDate, validationConfig.BlackoutWindows) || !IsBlackout(a.GameDate, validationConfig.BlackoutWindows))
            .OrderBy(a => SharesTeam(source, a) ? 1 : 0)
            .ThenBy(a => DateDistanceDays(sourceDate, ParseDate(a.GameDate)))
            .ThenBy(a => sourceStart.HasValue && ParseMinutes(a.StartTime).HasValue ? Math.Abs(sourceStart.Value - ParseMinutes(a.StartTime)!.Value) : int.MaxValue)
            .ThenBy(a => string.Equals(source.FieldKey, a.FieldKey, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(a => a.GameDate, StringComparer.OrdinalIgnoreCase)
            .ThenBy(a => a.StartTime, StringComparer.OrdinalIgnoreCase)
            .ThenBy(a => a.FieldKey, StringComparer.OrdinalIgnoreCase);
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

    private static IEnumerable<ScheduleAssignment> GetSourceAssignmentsForGroup(
        IReadOnlyList<ScheduleAssignment> assignments,
        ScheduleRuleGroupReport group,
        IReadOnlyDictionary<string, ScheduleAssignment> assignmentsBySlot)
    {
        var fromSlotIds = group.Violations
            .SelectMany(v => v.SlotIds)
            .Where(slotId => !string.IsNullOrWhiteSpace(slotId) && assignmentsBySlot.ContainsKey(slotId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(slotId => assignmentsBySlot[slotId])
            .ToList();
        if (fromSlotIds.Count > 0)
            return fromSlotIds;

        if (string.Equals(group.RuleId, "double-header", StringComparison.OrdinalIgnoreCase))
        {
            return group.Violations
                .SelectMany(v =>
                {
                    var teamId = ReadStringDetail(v.Details, "teamId");
                    var gameDate = ReadStringDetail(v.Details, "gameDate");
                    return assignments.Where(a =>
                        string.Equals(a.GameDate, gameDate, StringComparison.OrdinalIgnoreCase) &&
                        ContainsTeam(a, teamId));
                })
                .DistinctBy(a => a.SlotId, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        if (string.Equals(group.RuleId, "max-games-per-week", StringComparison.OrdinalIgnoreCase))
        {
            return group.Violations
                .SelectMany(v =>
                {
                    var teamId = ReadStringDetail(v.Details, "teamId");
                    var week = ReadStringDetail(v.Details, "week");
                    return assignments.Where(a =>
                        string.Equals(WeekKey(a.GameDate), week, StringComparison.OrdinalIgnoreCase) &&
                        ContainsTeam(a, teamId));
                })
                .DistinctBy(a => a.SlotId, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return Array.Empty<ScheduleAssignment>();
    }

    private static bool ContainsTeam(ScheduleAssignment a, string teamId)
    {
        if (string.IsNullOrWhiteSpace(teamId)) return false;
        return string.Equals(a.HomeTeamId, teamId, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(a.AwayTeamId, teamId, StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadStringDetail(Dictionary<string, object?> details, string key)
    {
        if (details is null || !details.TryGetValue(key, out var raw) || raw is null) return "";
        return raw.ToString() ?? "";
    }

    private static bool SharesTeam(ScheduleAssignment a, ScheduleAssignment b)
    {
        if (ContainsTeam(b, a.HomeTeamId)) return true;
        if (ContainsTeam(b, a.AwayTeamId)) return true;
        return false;
    }

    private static void AddTeams(HashSet<string> teamIds, ScheduleAssignment a)
    {
        if (!string.IsNullOrWhiteSpace(a.HomeTeamId)) teamIds.Add(a.HomeTeamId);
        if (!string.IsNullOrWhiteSpace(a.AwayTeamId)) teamIds.Add(a.AwayTeamId);
    }

    private static void AddWeek(HashSet<string> weeks, string gameDate)
    {
        var week = WeekKey(gameDate);
        if (!string.IsNullOrWhiteSpace(week)) weeks.Add(week);
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

    private static string PairKey(string teamA, string teamB)
    {
        if (string.IsNullOrWhiteSpace(teamA) || string.IsNullOrWhiteSpace(teamB)) return "";
        return string.Compare(teamA, teamB, StringComparison.OrdinalIgnoreCase) <= 0
            ? $"{teamA}|{teamB}"
            : $"{teamB}|{teamA}";
    }

    private static ScheduleRepairProposal AnnotatePriorityPairImpact(
        ScheduleRepairProposal proposal,
        IReadOnlyDictionary<string, int>? matchupPriorityByPair)
    {
        if (matchupPriorityByPair is null || matchupPriorityByPair.Count == 0)
            return proposal;

        var totalLater = 0;
        var totalEarlier = 0;
        var sameDate = 0;
        double weightedDelta = 0;

        foreach (var change in proposal.Changes ?? Array.Empty<ScheduleDiffChange>())
        {
            if (!string.Equals(change.ChangeType, "move", StringComparison.OrdinalIgnoreCase))
                continue;

            var homeTeamId = ReadStringObjectProperty(change.After, "homeTeamId");
            if (string.IsNullOrWhiteSpace(homeTeamId))
                homeTeamId = ReadStringObjectProperty(change.Before, "homeTeamId");
            var awayTeamId = ReadStringObjectProperty(change.After, "awayTeamId");
            if (string.IsNullOrWhiteSpace(awayTeamId))
                awayTeamId = ReadStringObjectProperty(change.Before, "awayTeamId");

            if (string.IsNullOrWhiteSpace(homeTeamId) || string.IsNullOrWhiteSpace(awayTeamId))
                continue;

            var pairKey = PairKey(homeTeamId, awayTeamId);
            if (string.IsNullOrWhiteSpace(pairKey)) continue;
            if (!matchupPriorityByPair.TryGetValue(pairKey, out var weight) || weight <= 0) continue;

            var beforeDate = ReadStringObjectProperty(change.Before, "gameDate");
            var afterDate = ReadStringObjectProperty(change.After, "gameDate");
            if (!TryParseDateOnly(beforeDate, out var before) || !TryParseDateOnly(afterDate, out var after))
                continue;

            var dayDelta = after.DayNumber - before.DayNumber;
            if (dayDelta > 0)
            {
                totalLater += 1;
                weightedDelta += weight * Math.Min(6.0, dayDelta / 7.0);
            }
            else if (dayDelta < 0)
            {
                totalEarlier += 1;
                weightedDelta -= weight * Math.Min(6.0, Math.Abs(dayDelta) / 7.0);
            }
            else
            {
                sameDate += 1;
            }
        }

        if (totalLater == 0 && totalEarlier == 0 && sameDate == 0)
            return proposal;

        var summaryParts = new List<string>();
        if (totalLater > 0) summaryParts.Add($"{totalLater} priority pair move(s) later");
        if (totalEarlier > 0) summaryParts.Add($"{totalEarlier} priority pair move(s) earlier");
        if (sameDate > 0 && summaryParts.Count == 0) summaryParts.Add($"{sameDate} priority pair move(s) without date change");
        var summary = string.Join("; ", summaryParts);

        var updatedSummary = new Dictionary<string, object?>(proposal.BeforeAfterSummary ?? new Dictionary<string, object?>(), StringComparer.OrdinalIgnoreCase)
        {
            ["priorityPairsLater"] = totalLater,
            ["priorityPairsEarlier"] = totalEarlier,
            ["priorityPairsSameDate"] = sameDate,
            ["priorityImpactWeightDelta"] = Math.Round(weightedDelta, 3),
            ["priorityImpactSummary"] = summary
        };

        var rationale = proposal.Rationale ?? "";
        if (!string.IsNullOrWhiteSpace(summary) && !rationale.Contains("priority", StringComparison.OrdinalIgnoreCase))
        {
            rationale = string.IsNullOrWhiteSpace(rationale)
                ? $"Priority pair impact: {summary}."
                : $"{rationale} Priority pair impact: {summary}.";
        }

        return proposal with
        {
            Rationale = rationale,
            BeforeAfterSummary = updatedSummary
        };
    }

    private static int GetPriorityImpactLaterCount(ScheduleRepairProposal proposal)
        => ReadSummaryInt(proposal.BeforeAfterSummary, "priorityPairsLater");

    private static int GetPriorityImpactEarlierCount(ScheduleRepairProposal proposal)
        => ReadSummaryInt(proposal.BeforeAfterSummary, "priorityPairsEarlier");

    private static double GetPriorityImpactWeightDelta(ScheduleRepairProposal proposal)
        => ReadSummaryDouble(proposal.BeforeAfterSummary, "priorityImpactWeightDelta");

    private static int ReadSummaryInt(IReadOnlyDictionary<string, object?>? summary, string key)
    {
        if (summary is null || !summary.TryGetValue(key, out var raw) || raw is null) return 0;
        if (raw is int i) return i;
        if (raw is long l) return (int)l;
        if (raw is JsonElement el)
        {
            if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var parsed)) return parsed;
            if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), out var parsedText)) return parsedText;
        }
        return int.TryParse(raw.ToString(), out var result) ? result : 0;
    }

    private static double ReadSummaryDouble(IReadOnlyDictionary<string, object?>? summary, string key)
    {
        if (summary is null || !summary.TryGetValue(key, out var raw) || raw is null) return 0;
        if (raw is double d) return d;
        if (raw is float f) return f;
        if (raw is decimal m) return (double)m;
        if (raw is JsonElement el)
        {
            if (el.ValueKind == JsonValueKind.Number && el.TryGetDouble(out var parsed)) return parsed;
            if (el.ValueKind == JsonValueKind.String && double.TryParse(el.GetString(), out var parsedText)) return parsedText;
        }
        return double.TryParse(raw.ToString(), out var result) ? result : 0;
    }

    private static string ReadStringObjectProperty(object? value, string property)
    {
        if (value is null) return "";
        if (value is JsonElement el)
        {
            if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty(property, out var prop))
                return prop.ToString() ?? "";
            return "";
        }

        try
        {
            var info = value.GetType().GetProperty(property);
            var raw = info?.GetValue(value, null);
            return raw?.ToString() ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static bool TryParseDateOnly(string raw, out DateOnly date)
        => DateOnly.TryParseExact(raw ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
}
