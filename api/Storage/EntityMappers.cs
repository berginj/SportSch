using Azure.Data.Tables;

namespace GameSwap.Functions.Storage;

/// <summary>
/// Maps TableEntity objects to response DTOs.
/// </summary>
public static class EntityMappers
{
    /// <summary>
    /// Maps a slot TableEntity to an API response object.
    /// </summary>
    public static object MapSlot(TableEntity e)
    {
        var allocationPriority = ReadPositiveInt(e, "AllocationPriorityRank");
        return new
        {
            slotId = e.RowKey,
            leagueId = ReadString(e, "LeagueId"),
            division = ReadString(e, "Division"),
            offeringTeamId = ReadString(e, "OfferingTeamId"),
            homeTeamId = ReadString(e, "HomeTeamId"),
            awayTeamId = ReadString(e, "AwayTeamId"),
            isExternalOffer = ReadBool(e, "IsExternalOffer", false),
            isAvailability = ReadBool(e, "IsAvailability", false),
            offeringEmail = ReadString(e, "OfferingEmail"),
            gameDate = ReadString(e, "GameDate"),
            startTime = ReadString(e, "StartTime"),
            endTime = ReadString(e, "EndTime"),
            parkName = ReadString(e, "ParkName"),
            fieldName = ReadString(e, "FieldName"),
            displayName = ReadString(e, "DisplayName"),
            fieldKey = ReadString(e, "FieldKey"),
            gameType = ReadString(e, "GameType"),
            allocationSlotType = ReadString(e, "AllocationSlotType"),
            allocationPriorityRank = allocationPriority,
            status = ReadString(e, "Status", Constants.Status.SlotOpen),
            notes = ReadString(e, "Notes"),
            createdUtc = ReadDateTimeOffset(e, "CreatedUtc"),
            updatedUtc = ReadDateTimeOffset(e, "UpdatedUtc")
        };
    }

    /// <summary>
    /// Maps an availability rule TableEntity to an API response object.
    /// </summary>
    public static object MapAvailabilityRule(TableEntity e)
    {
        return new
        {
            ruleId = e.RowKey,
            fieldKey = ReadString(e, Constants.FieldAvailabilityColumns.FieldKey),
            division = ReadString(e, Constants.FieldAvailabilityColumns.Division),
            divisionIds = SplitList(ReadString(e, Constants.FieldAvailabilityColumns.DivisionIds)),
            startsOn = ReadString(e, Constants.FieldAvailabilityColumns.StartsOn),
            endsOn = ReadString(e, Constants.FieldAvailabilityColumns.EndsOn),
            daysOfWeek = SplitList(ReadString(e, Constants.FieldAvailabilityColumns.DaysOfWeek)),
            startTimeLocal = ReadString(e, Constants.FieldAvailabilityColumns.StartTimeLocal),
            endTimeLocal = ReadString(e, Constants.FieldAvailabilityColumns.EndTimeLocal),
            recurrencePattern = ReadString(e, Constants.FieldAvailabilityColumns.RecurrencePattern),
            timezone = ReadString(e, Constants.FieldAvailabilityColumns.Timezone),
            isActive = ReadBool(e, Constants.FieldAvailabilityColumns.IsActive, false)
        };
    }

    /// <summary>
    /// Maps an availability exception TableEntity to an API response object.
    /// </summary>
    public static object MapAvailabilityException(TableEntity e)
    {
        return new
        {
            exceptionId = e.RowKey,
            dateFrom = ReadString(e, Constants.FieldAvailabilityExceptionColumns.DateFrom),
            dateTo = ReadString(e, Constants.FieldAvailabilityExceptionColumns.DateTo),
            startTimeLocal = ReadString(e, Constants.FieldAvailabilityExceptionColumns.StartTimeLocal),
            endTimeLocal = ReadString(e, Constants.FieldAvailabilityExceptionColumns.EndTimeLocal),
            reason = ReadString(e, Constants.FieldAvailabilityExceptionColumns.Reason)
        };
    }

    /// <summary>
    /// Maps a slot request TableEntity to an API response object.
    /// </summary>
    public static object MapSlotRequest(TableEntity e)
    {
        return new
        {
            requestId = e.RowKey,
            slotId = ReadString(e, "SlotId"),
            requestingTeamId = ReadString(e, "RequestingTeamId"),
            requestingUserId = ReadString(e, "RequestingUserId"),
            requestingEmail = ReadString(e, "RequestingEmail"),
            status = ReadString(e, "Status", "Pending"),
            message = ReadString(e, "Message"),
            createdUtc = ReadDateTimeOffset(e, "CreatedUtc"),
            updatedUtc = ReadDateTimeOffset(e, "UpdatedUtc")
        };
    }

    /// <summary>
    /// Maps a field TableEntity to an API response object.
    /// </summary>
    public static object MapField(TableEntity e)
    {
        return new
        {
            parkCode = ReadString(e, "ParkCode"),
            fieldCode = ReadString(e, "FieldCode"),
            parkName = ReadString(e, "ParkName"),
            fieldName = ReadString(e, "FieldName"),
            displayName = ReadString(e, "DisplayName"),
            fieldKey = ReadString(e, "FieldKey"),
            isActive = ReadBool(e, "IsActive", true)
        };
    }

    /// <summary>
    /// Maps a membership TableEntity to an API response object.
    /// </summary>
    public static object MapMembership(TableEntity e)
    {
        return new
        {
            userId = e.PartitionKey,
            leagueId = e.RowKey,
            role = ReadString(e, "Role", Constants.Roles.Viewer),
            coachDivision = ReadString(e, "CoachDivision"),
            coachTeamId = ReadString(e, "CoachTeamId"),
            joinedUtc = ReadDateTimeOffset(e, "JoinedUtc")
        };
    }

    /// <summary>
    /// Helper to split comma-separated list values.
    /// </summary>
    private static List<string> SplitList(string? value)
    {
        return (value ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .ToList();
    }

    private static object? ReadValue(TableEntity entity, string key)
        => entity.TryGetValue(key, out var value) ? value : null;

    private static string ReadString(TableEntity entity, string key, string defaultValue = "")
    {
        var value = ReadValue(entity, key);
        if (value is null) return defaultValue;
        var text = value.ToString();
        return string.IsNullOrWhiteSpace(text) ? defaultValue : text.Trim();
    }

    private static bool ReadBool(TableEntity entity, string key, bool defaultValue)
    {
        var value = ReadValue(entity, key);
        if (value is null) return defaultValue;
        if (value is bool b) return b;
        var text = value.ToString()?.Trim() ?? "";
        if (bool.TryParse(text, out var parsedBool)) return parsedBool;
        if (int.TryParse(text, out var parsedInt)) return parsedInt != 0;
        return defaultValue;
    }

    private static int? ReadPositiveInt(TableEntity entity, string key)
    {
        var value = ReadValue(entity, key);
        if (value is null) return null;

        int? parsed = value switch
        {
            int i => i,
            long l when l <= int.MaxValue => (int)l,
            double d => (int)Math.Round(d),
            _ => int.TryParse(value.ToString(), out var intParsed) ? intParsed : null
        };

        if (!parsed.HasValue || parsed.Value <= 0) return null;
        return parsed.Value;
    }

    private static DateTimeOffset? ReadDateTimeOffset(TableEntity entity, string key)
    {
        var value = ReadValue(entity, key);
        if (value is null) return null;
        if (value is DateTimeOffset dto) return dto;
        if (value is DateTime dt) return new DateTimeOffset(dt);
        if (DateTimeOffset.TryParse(value.ToString(), out var parsed)) return parsed;
        return null;
    }
}
