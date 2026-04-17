using System.Globalization;
using System.Net;
using Azure.Data.Tables;
using GameSwap.Functions.Repositories;
using GameSwap.Functions.Storage;
using Microsoft.Extensions.Logging;

namespace GameSwap.Functions.Services;

/// <summary>
/// Extension methods for simplified calendar-integrated practice requests.
/// Supports auto-approval logic and real-time conflict detection.
/// </summary>
public static class SimplePracticeRequestExtensions
{
    /// <summary>
    /// Creates a simplified practice request from calendar interaction.
    /// Automatically approves if no conflicts detected.
    /// </summary>
    public static async Task<SimplePracticeRequestResult> CreateSimplePracticeRequestAsync(
        this IPracticeRequestService service,
        SimplePracticeRequestParams request,
        string leagueId,
        string userId,
        string teamId,
        IMembershipRepository membershipRepo,
        IPracticeRequestRepository practiceRequestRepo,
        ILogger logger)
    {
        // Validate inputs
        if (string.IsNullOrWhiteSpace(request.FieldKey))
            throw new ApiGuards.HttpError(400, ErrorCodes.BAD_REQUEST, "Field is required");
        if (string.IsNullOrWhiteSpace(request.Date))
            throw new ApiGuards.HttpError(400, ErrorCodes.BAD_REQUEST, "Date is required");
        if (string.IsNullOrWhiteSpace(request.StartTime))
            throw new ApiGuards.HttpError(400, ErrorCodes.BAD_REQUEST, "Start time is required");
        if (string.IsNullOrWhiteSpace(request.EndTime))
            throw new ApiGuards.HttpError(400, ErrorCodes.BAD_REQUEST, "End time is required");

        // Check for conflicts
        var conflicts = await CheckSimplePracticeConflictsAsync(
            request.FieldKey,
            request.Date,
            request.StartTime,
            request.EndTime,
            request.Policy,
            leagueId,
            teamId,
            practiceRequestRepo,
            logger
        );

        // Determine if can auto-approve
        var canAutoApprove = DetermineAutoApproval(request.Policy, conflicts);

        // Create practice request entity
        var entity = new TableEntity
        {
            PartitionKey = $"PRACTICE|{leagueId}",
            RowKey = Guid.NewGuid().ToString(),
            ["FieldKey"] = request.FieldKey,
            ["Date"] = request.Date,
            ["StartTime"] = request.StartTime,
            ["EndTime"] = request.EndTime,
            ["TeamId"] = teamId,
            ["RequestedBy"] = userId,
            ["Policy"] = request.Policy ?? "shared",
            ["Status"] = canAutoApprove ? "Approved" : "Pending",
            ["AutoApproved"] = canAutoApprove,
            ["Notes"] = request.Notes ?? "",
            ["CreatedUtc"] = DateTimeOffset.UtcNow,
            ["UpdatedUtc"] = DateTimeOffset.UtcNow
        };

        // Save to repository
        await practiceRequestRepo.CreateRequestAsync(entity);

        logger.LogInformation(
            "Practice request created: {RequestId}, Field: {FieldKey}, Date: {Date}, Status: {Status}, AutoApproved: {AutoApproved}",
            entity.RowKey,
            request.FieldKey,
            request.Date,
            entity["Status"],
            canAutoApprove
        );

        return new SimplePracticeRequestResult
        {
            RequestId = entity.RowKey,
            FieldKey = request.FieldKey,
            Date = request.Date,
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            Policy = entity.GetString("Policy") ?? "shared",
            Status = entity.GetString("Status") ?? "Pending",
            AutoApproved = canAutoApprove,
            Conflicts = conflicts,
            CreatedUtc = entity.Timestamp ?? DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Checks for conflicts with existing practice requests.
    /// </summary>
    public static async Task<List<PracticeConflict>> CheckSimplePracticeConflictsAsync(
        string fieldKey,
        string date,
        string startTime,
        string endTime,
        string? policy,
        string leagueId,
        string? excludeTeamId,
        IPracticeRequestRepository practiceRequestRepo,
        ILogger logger)
    {
        var conflicts = new List<PracticeConflict>();

        try
        {
            // Parse times for overlap checking
            var requestStart = ParseTime(startTime);
            var requestEnd = ParseTime(endTime);

            if (requestStart == null || requestEnd == null)
            {
                logger.LogWarning("Invalid time format: {StartTime} - {EndTime}", startTime, endTime);
                return conflicts;
            }

            // Get all practice requests for this field on this date
            var existingRequests = await practiceRequestRepo.GetRequestsByFieldAndDateAsync(
                leagueId,
                fieldKey,
                date
            );

            foreach (var existing in existingRequests)
            {
                var existingStatus = existing.GetString("Status") ?? "";
                if (existingStatus != "Approved" && existingStatus != "Pending")
                    continue; // Skip cancelled/rejected requests

                var existingTeamId = existing.GetString("TeamId") ?? "";
                if (!string.IsNullOrWhiteSpace(excludeTeamId) &&
                    string.Equals(existingTeamId, excludeTeamId, StringComparison.OrdinalIgnoreCase))
                    continue; // Skip same team (moving practice time)

                var existingStart = ParseTime(existing.GetString("StartTime") ?? "");
                var existingEnd = ParseTime(existing.GetString("EndTime") ?? "");

                if (existingStart == null || existingEnd == null)
                    continue;

                // Check for time overlap
                var hasOverlap = requestStart < existingEnd && requestEnd > existingStart;

                if (hasOverlap)
                {
                    conflicts.Add(new PracticeConflict
                    {
                        RequestId = existing.RowKey,
                        TeamId = existingTeamId,
                        TeamName = existingTeamId, // TODO: Look up team name
                        StartTime = existing.GetString("StartTime") ?? "",
                        EndTime = existing.GetString("EndTime") ?? "",
                        Policy = existing.GetString("Policy") ?? "shared",
                        Status = existingStatus
                    });
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error checking practice conflicts for field {FieldKey} on {Date}", fieldKey, date);
        }

        return conflicts;
    }

    /// <summary>
    /// Determines if a practice request can be auto-approved based on conflicts.
    /// </summary>
    private static bool DetermineAutoApproval(string? policy, List<PracticeConflict> conflicts)
    {
        var normalizedPolicy = (policy ?? "shared").Trim().ToLowerInvariant();

        // Rule 1: No conflicts at all -> Auto-approve
        if (conflicts.Count == 0)
            return true;

        // Rule 2: Shared booking AND all existing are also shared -> Auto-approve
        if (normalizedPolicy == "shared" && conflicts.All(c => c.Policy.ToLowerInvariant() == "shared"))
            return true;

        // Rule 3: Any exclusive booking or conflict with exclusive -> Require admin approval
        return false;
    }

    /// <summary>
    /// Parses HH:mm time format to minutes since midnight for comparison.
    /// </summary>
    private static int? ParseTime(string time)
    {
        if (string.IsNullOrWhiteSpace(time))
            return null;

        var parts = time.Split(':');
        if (parts.Length != 2)
            return null;

        if (!int.TryParse(parts[0], out var hours) || !int.TryParse(parts[1], out var minutes))
            return null;

        if (hours < 0 || hours > 23 || minutes < 0 || minutes > 59)
            return null;

        return hours * 60 + minutes;
    }
}

/// <summary>
/// Request parameters for simplified practice request.
/// </summary>
public record SimplePracticeRequestParams
{
    public required string FieldKey { get; init; }
    public required string Date { get; init; }
    public required string StartTime { get; init; }
    public required string EndTime { get; init; }
    public string? Policy { get; init; } = "shared";
    public string? Notes { get; init; }
}

/// <summary>
/// Result of creating a simplified practice request.
/// </summary>
public record SimplePracticeRequestResult
{
    public required string RequestId { get; init; }
    public required string FieldKey { get; init; }
    public required string Date { get; init; }
    public required string StartTime { get; init; }
    public required string EndTime { get; init; }
    public required string Policy { get; init; }
    public required string Status { get; init; }
    public required bool AutoApproved { get; init; }
    public required List<PracticeConflict> Conflicts { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }
}

/// <summary>
/// Represents a practice request conflict.
/// </summary>
public record PracticeConflict
{
    public required string RequestId { get; init; }
    public required string TeamId { get; init; }
    public required string TeamName { get; init; }
    public required string StartTime { get; init; }
    public required string EndTime { get; init; }
    public required string Policy { get; init; }
    public required string Status { get; init; }
}
