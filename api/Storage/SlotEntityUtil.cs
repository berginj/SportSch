using Azure.Data.Tables;

namespace GameSwap.Functions.Storage;

/// <summary>
/// Shared slot entity parsing, classification, and scheduler mutation helpers.
/// Keeps availability/practice classification logic consistent across scheduler,
/// clear/reset tools, and practice workflows.
/// </summary>
public static class SlotEntityUtil
{
    public static string ReadString(TableEntity entity, string key, string defaultValue = "")
    {
        if (!entity.TryGetValue(key, out var value) || value is null) return defaultValue;
        var text = value.ToString();
        return string.IsNullOrWhiteSpace(text) ? defaultValue : text.Trim();
    }

    public static bool ReadBool(TableEntity entity, string key, bool defaultValue)
    {
        if (!entity.TryGetValue(key, out var value) || value is null) return defaultValue;
        if (value is bool b) return b;
        var text = value.ToString()?.Trim() ?? "";
        if (bool.TryParse(text, out var parsedBool)) return parsedBool;
        if (int.TryParse(text, out var parsedInt)) return parsedInt != 0;
        return defaultValue;
    }

    public static bool IsAvailability(TableEntity entity)
    {
        if (IsPractice(entity)) return false;
        if (ReadBool(entity, "IsAvailability", false)) return true;
        var gameType = ReadString(entity, "GameType");
        return string.Equals(gameType, "Availability", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsPractice(TableEntity entity)
    {
        var gameType = ReadString(entity, "GameType");
        return string.Equals(gameType, "Practice", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsPracticeRequestableAvailability(TableEntity slot)
    {
        if (!IsAvailability(slot)) return false;

        var allocationSlotType = ReadString(slot, "AllocationSlotType");
        if (string.IsNullOrWhiteSpace(allocationSlotType))
            allocationSlotType = ReadString(slot, "SlotType");
        if (string.IsNullOrWhiteSpace(allocationSlotType))
            return true;

        return string.Equals(allocationSlotType, "practice", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(allocationSlotType, "both", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsReusableSchedulerGameSlot(TableEntity entity)
    {
        var status = ReadString(entity, "Status", Constants.Status.SlotOpen);
        var isOpen = string.Equals(status, Constants.Status.SlotOpen, StringComparison.OrdinalIgnoreCase);
        var isConfirmed = string.Equals(status, Constants.Status.SlotConfirmed, StringComparison.OrdinalIgnoreCase);
        if (!isOpen && !isConfirmed) return false;

        if (IsPractice(entity)) return false;
        if (ReadBool(entity, "IsAvailability", false)) return false;

        var gameType = ReadString(entity, "GameType");
        var scheduleRunId = ReadString(entity, "ScheduleRunId");
        return !string.IsNullOrWhiteSpace(scheduleRunId) ||
               string.Equals(gameType, "Availability", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Applies the common scheduler game/external-offer mutation semantics used by both
    /// legacy scheduler and wizard scheduler endpoints.
    /// </summary>
    public static void ApplySchedulerAssignment(
        TableEntity slot,
        string runId,
        string homeTeamId,
        string awayTeamId,
        bool isExternalOffer,
        string confirmedBy,
        DateTimeOffset nowUtc)
    {
        var home = (homeTeamId ?? "").Trim();
        var away = (awayTeamId ?? "").Trim();

        slot["OfferingTeamId"] = home;
        slot["HomeTeamId"] = home;
        slot["AwayTeamId"] = isExternalOffer ? "" : away;
        slot["IsExternalOffer"] = isExternalOffer;
        slot["IsAvailability"] = false;
        slot["PendingRequestId"] = "";
        slot["PendingTeamId"] = "";
        slot["ScheduleRunId"] = runId;

        if (isExternalOffer)
        {
            slot["Status"] = Constants.Status.SlotOpen;
            slot["ConfirmedTeamId"] = "";
            slot["ConfirmedRequestId"] = "";
            slot["ConfirmedBy"] = "";
            slot["ConfirmedUtc"] = "";
        }
        else
        {
            slot["Status"] = Constants.Status.SlotConfirmed;
            slot["ConfirmedTeamId"] = away;
            slot["ConfirmedRequestId"] = "";
            slot["ConfirmedBy"] = (confirmedBy ?? "").Trim();
            slot["ConfirmedUtc"] = nowUtc;
        }

        slot["UpdatedUtc"] = nowUtc;
    }

    public static void ResetSchedulerSlotToAvailability(TableEntity slot, DateTimeOffset nowUtc)
    {
        slot["HomeTeamId"] = "";
        slot["AwayTeamId"] = "";
        slot["IsExternalOffer"] = false;
        slot["IsAvailability"] = true;
        slot["Status"] = Constants.Status.SlotOpen;
        slot["GameType"] = "Availability";
        slot["ScheduleRunId"] = "";
        slot["ConfirmedTeamId"] = "";
        slot["ConfirmedRequestId"] = "";
        slot["ConfirmedBy"] = "";
        slot["ConfirmedUtc"] = "";
        slot["PendingTeamId"] = "";
        slot["PendingRequestId"] = "";
        slot["UpdatedUtc"] = nowUtc;
    }
}
