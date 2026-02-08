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
        return new
        {
            slotId = e.RowKey,
            leagueId = e.GetString("LeagueId") ?? "",
            division = e.GetString("Division") ?? "",
            offeringTeamId = e.GetString("OfferingTeamId") ?? "",
            homeTeamId = e.GetString("HomeTeamId") ?? "",
            awayTeamId = e.GetString("AwayTeamId") ?? "",
            isExternalOffer = e.GetBoolean("IsExternalOffer") ?? false,
            isAvailability = e.GetBoolean("IsAvailability") ?? false,
            offeringEmail = e.GetString("OfferingEmail") ?? "",
            gameDate = e.GetString("GameDate") ?? "",
            startTime = e.GetString("StartTime") ?? "",
            endTime = e.GetString("EndTime") ?? "",
            parkName = e.GetString("ParkName") ?? "",
            fieldName = e.GetString("FieldName") ?? "",
            displayName = e.GetString("DisplayName") ?? "",
            fieldKey = e.GetString("FieldKey") ?? "",
            gameType = e.GetString("GameType") ?? "",
            allocationSlotType = e.GetString("AllocationSlotType") ?? "",
            allocationPriorityRank = (
                (e.GetInt32("AllocationPriorityRank") is int rank && rank > 0)
                    ? rank
                    : (int.TryParse(e.GetString("AllocationPriorityRank"), out var parsed) && parsed > 0 ? parsed : (int?)null)
            ),
            status = e.GetString("Status") ?? Constants.Status.SlotOpen,
            notes = e.GetString("Notes") ?? "",
            createdUtc = e.GetDateTimeOffset("CreatedUtc"),
            updatedUtc = e.GetDateTimeOffset("UpdatedUtc")
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
            fieldKey = e.GetString(Constants.FieldAvailabilityColumns.FieldKey) ?? "",
            division = e.GetString(Constants.FieldAvailabilityColumns.Division) ?? "",
            divisionIds = SplitList(e.GetString(Constants.FieldAvailabilityColumns.DivisionIds)),
            startsOn = e.GetString(Constants.FieldAvailabilityColumns.StartsOn) ?? "",
            endsOn = e.GetString(Constants.FieldAvailabilityColumns.EndsOn) ?? "",
            daysOfWeek = SplitList(e.GetString(Constants.FieldAvailabilityColumns.DaysOfWeek)),
            startTimeLocal = e.GetString(Constants.FieldAvailabilityColumns.StartTimeLocal) ?? "",
            endTimeLocal = e.GetString(Constants.FieldAvailabilityColumns.EndTimeLocal) ?? "",
            recurrencePattern = e.GetString(Constants.FieldAvailabilityColumns.RecurrencePattern) ?? "",
            timezone = e.GetString(Constants.FieldAvailabilityColumns.Timezone) ?? "",
            isActive = e.GetBoolean(Constants.FieldAvailabilityColumns.IsActive) ?? false
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
            dateFrom = e.GetString(Constants.FieldAvailabilityExceptionColumns.DateFrom) ?? "",
            dateTo = e.GetString(Constants.FieldAvailabilityExceptionColumns.DateTo) ?? "",
            startTimeLocal = e.GetString(Constants.FieldAvailabilityExceptionColumns.StartTimeLocal) ?? "",
            endTimeLocal = e.GetString(Constants.FieldAvailabilityExceptionColumns.EndTimeLocal) ?? "",
            reason = e.GetString(Constants.FieldAvailabilityExceptionColumns.Reason) ?? ""
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
            slotId = e.GetString("SlotId") ?? "",
            requestingTeamId = e.GetString("RequestingTeamId") ?? "",
            requestingUserId = e.GetString("RequestingUserId") ?? "",
            requestingEmail = e.GetString("RequestingEmail") ?? "",
            status = e.GetString("Status") ?? "Pending",
            message = e.GetString("Message") ?? "",
            createdUtc = e.GetDateTimeOffset("CreatedUtc"),
            updatedUtc = e.GetDateTimeOffset("UpdatedUtc")
        };
    }

    /// <summary>
    /// Maps a field TableEntity to an API response object.
    /// </summary>
    public static object MapField(TableEntity e)
    {
        return new
        {
            parkCode = e.GetString("ParkCode") ?? "",
            fieldCode = e.GetString("FieldCode") ?? "",
            parkName = e.GetString("ParkName") ?? "",
            fieldName = e.GetString("FieldName") ?? "",
            displayName = e.GetString("DisplayName") ?? "",
            fieldKey = e.GetString("FieldKey") ?? "",
            isActive = e.GetBoolean("IsActive") ?? true
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
            role = e.GetString("Role") ?? Constants.Roles.Viewer,
            coachDivision = e.GetString("CoachDivision") ?? "",
            coachTeamId = e.GetString("CoachTeamId") ?? "",
            joinedUtc = e.GetDateTimeOffset("JoinedUtc")
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
}
