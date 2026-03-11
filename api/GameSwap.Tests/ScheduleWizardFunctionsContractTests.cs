#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GameSwap.Functions.Functions;
using GameSwap.Functions.Scheduling;
using Xunit;

namespace GameSwap.Tests;

public class ScheduleWizardFunctionsContractTests
{
    [Fact]
    public void BuildRequiredGuestAnchorWarnings_SkipsFullyBlockedWeeks()
    {
        var slotInfoType = GetNestedType("SlotInfo");
        var blockedDateRangeType = GetNestedType("BlockedDateRange");
        var guestAnchorType = GetNestedType("GuestAnchor");
        var guestAnchorSetType = GetNestedType("GuestAnchorSet");

        var regularSlots = CreateTypedList(
            slotInfoType,
            CreateNestedRecord(
                slotInfoType,
                "slot-1",
                "2026-03-23",
                "19:00",
                "21:00",
                "barcroft/field-1",
                "",
                "game",
                1),
            CreateNestedRecord(
                slotInfoType,
                "slot-2",
                "2026-03-30",
                "19:00",
                "21:00",
                "barcroft/field-1",
                "",
                "game",
                1));
        var blockedRanges = CreateTypedList(
            blockedDateRangeType,
            CreateNestedRecord(
                blockedDateRangeType,
                new DateOnly(2026, 3, 16),
                new DateOnly(2026, 3, 22),
                "Spring break"));
        var guestAnchors = CreateNestedRecord(
            guestAnchorSetType,
            CreateNestedRecord(
                guestAnchorType,
                DayOfWeek.Monday,
                "19:00",
                "21:00",
                "barcroft/field-1"),
            null);

        var warnings = InvokePrivateStatic(
            "BuildRequiredGuestAnchorWarnings",
            new DateOnly(2026, 3, 14),
            new DateOnly(2026, 3, 31),
            regularSlots,
            1,
            guestAnchors,
            blockedRanges);

        Assert.Empty(ToObjectList(warnings));
    }

    [Fact]
    public void OrderSlotsByPreference_BackwardModeUsesDateBeforePriority()
    {
        var slotInfoType = GetNestedType("SlotInfo");
        var slots = CreateTypedList(
            slotInfoType,
            CreateNestedRecord(
                slotInfoType,
                "early-high-priority",
                "2026-03-15",
                "18:00",
                "20:00",
                "field-1",
                "",
                "game",
                1),
            CreateNestedRecord(
                slotInfoType,
                "late-lower-priority",
                "2026-05-31",
                "18:00",
                "20:00",
                "field-1",
                "",
                "game",
                5));

        var ordered = InvokePrivateStatic(
            "OrderSlotsByPreference",
            slots,
            new List<DayOfWeek>(),
            true);

        var orderedSlots = ToObjectList(ordered).Cast<ScheduleSlot>().ToList();
        Assert.Equal(2, orderedSlots.Count);
        Assert.Equal("late-lower-priority", orderedSlots[0].SlotId);
        Assert.Equal("early-high-priority", orderedSlots[1].SlotId);
    }

    [Fact]
    public void BuildRepeatedMatchups_AllowsParityExtraGameForOddTeamTargets()
    {
        var teams = new List<string> { "Team-1", "Team-2", "Team-3", "Team-4", "Team-5" };

        var matchups = (List<MatchupPair>)InvokePrivateStatic(
            "BuildRepeatedMatchups",
            teams,
            11);

        Assert.Equal(28, matchups.Count);

        var counts = teams.ToDictionary(t => t, _ => 0, StringComparer.OrdinalIgnoreCase);
        foreach (var matchup in matchups)
        {
            counts[matchup.HomeTeamId] += 1;
            counts[matchup.AwayTeamId] += 1;
        }

        Assert.All(counts.Values, total => Assert.True(total >= 11));
        Assert.Contains(12, counts.Values);
    }

    private static Type GetNestedType(string name) =>
        typeof(ScheduleWizardFunctions).GetNestedType(name, BindingFlags.NonPublic)
        ?? throw new InvalidOperationException($"Nested type '{name}' not found.");

    private static object CreateNestedRecord(Type type, params object?[] args) =>
        Activator.CreateInstance(type, args)
        ?? throw new InvalidOperationException($"Failed to create instance of '{type.FullName}'.");

    private static object CreateTypedList(Type itemType, params object[] items)
    {
        var listType = typeof(List<>).MakeGenericType(itemType);
        var list = (IList)(Activator.CreateInstance(listType)
            ?? throw new InvalidOperationException($"Failed to create list for '{itemType.FullName}'."));
        foreach (var item in items)
        {
            list.Add(item);
        }

        return list;
    }

    private static object InvokePrivateStatic(string methodName, params object?[] args)
    {
        var method = typeof(ScheduleWizardFunctions).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException($"Method '{methodName}' not found.");
        return method.Invoke(null, args)
            ?? throw new InvalidOperationException($"Method '{methodName}' returned null.");
    }

    private static List<object> ToObjectList(object value) =>
        ((IEnumerable)value).Cast<object>().ToList();
}
