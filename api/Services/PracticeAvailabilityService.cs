using System.Globalization;
using Azure.Data.Tables;
using GameSwap.Functions.Models;
using GameSwap.Functions.Repositories;
using GameSwap.Functions.Storage;

namespace GameSwap.Functions.Services;

public class PracticeAvailabilityService : IPracticeAvailabilityService
{
    private static readonly string[] ActiveRequestStatuses =
    [
        FieldInventoryPracticeRequestStatuses.Pending,
        FieldInventoryPracticeRequestStatuses.Approved,
    ];

    private readonly IMembershipRepository _membershipRepository;
    private readonly ITeamRepository _teamRepository;
    private readonly ISlotRepository _slotRepository;
    private readonly IPracticeRequestRepository _practiceRequestRepository;

    public PracticeAvailabilityService(
        IMembershipRepository membershipRepository,
        ITeamRepository teamRepository,
        ISlotRepository slotRepository,
        IPracticeRequestRepository practiceRequestRepository)
    {
        _membershipRepository = membershipRepository;
        _teamRepository = teamRepository;
        _slotRepository = slotRepository;
        _practiceRequestRepository = practiceRequestRepository;
    }

    public async Task<PracticeAvailabilityOptionsResponse> GetCoachAvailabilityOptionsAsync(
        PracticeAvailabilityQueryRequest request,
        string userId,
        CorrelationContext context)
    {
        var actor = await ResolveActorAsync(context.LeagueId, userId, request.Division);
        var date = RequireIsoDate(request.Date, "date");
        var startTime = NormalizeTimeOrEmpty(request.StartTime);
        var endTime = NormalizeTimeOrEmpty(request.EndTime);
        if (!string.IsNullOrWhiteSpace(startTime) || !string.IsNullOrWhiteSpace(endTime))
        {
            ValidateTimeRange(startTime, endTime);
        }

        var fieldKey = NormalizeFieldKey(request.FieldKey);
        var slots = await LoadCandidateSlotsAsync(context.LeagueId, actor.Division, date, fieldKey, request.SeasonLabel);
        var requests = await _practiceRequestRepository.QueryRequestsAsync(context.LeagueId, null, actor.Division, null, null);
        var options = slots
            .Where(slot => string.IsNullOrWhiteSpace(startTime) || string.Equals(SlotEntityUtil.ReadString(slot, "StartTime"), startTime, StringComparison.OrdinalIgnoreCase))
            .Where(slot => string.IsNullOrWhiteSpace(endTime) || string.Equals(SlotEntityUtil.ReadString(slot, "EndTime"), endTime, StringComparison.OrdinalIgnoreCase))
            .Select(slot => MapOption(slot, requests))
            .OrderBy(option => option.Date)
            .ThenBy(option => option.StartTime)
            .ThenBy(option => option.FieldName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new PracticeAvailabilityOptionsResponse(
            FirstNonBlank(request.SeasonLabel, options.FirstOrDefault()?.SeasonLabel, ""),
            actor.Division,
            actor.TeamId,
            actor.TeamName,
            date,
            string.IsNullOrWhiteSpace(startTime) ? null : startTime,
            string.IsNullOrWhiteSpace(endTime) ? null : endTime,
            string.IsNullOrWhiteSpace(fieldKey) ? null : fieldKey,
            !string.IsNullOrWhiteSpace(startTime) && !string.IsNullOrWhiteSpace(endTime),
            options.Count,
            options);
    }

    public async Task<PracticeAvailabilityCheckResponse> CheckCoachAvailabilityAsync(
        PracticeAvailabilityQueryRequest request,
        string userId,
        CorrelationContext context)
    {
        var actor = await ResolveActorAsync(context.LeagueId, userId, request.Division);
        var date = RequireIsoDate(request.Date, "date");
        var startTime = NormalizeRequiredTime(request.StartTime, "startTime");
        var endTime = NormalizeRequiredTime(request.EndTime, "endTime");
        ValidateTimeRange(startTime, endTime);

        var fieldKey = NormalizeFieldKey(request.FieldKey);
        var slots = await LoadCandidateSlotsAsync(context.LeagueId, actor.Division, date, fieldKey, request.SeasonLabel);
        var requests = await _practiceRequestRepository.QueryRequestsAsync(context.LeagueId, null, actor.Division, null, null);
        var options = slots
            .Where(slot => string.Equals(SlotEntityUtil.ReadString(slot, "StartTime"), startTime, StringComparison.OrdinalIgnoreCase))
            .Where(slot => string.Equals(SlotEntityUtil.ReadString(slot, "EndTime"), endTime, StringComparison.OrdinalIgnoreCase))
            .Select(slot => MapOption(slot, requests))
            .OrderBy(option => option.FieldName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new PracticeAvailabilityCheckResponse(
            FirstNonBlank(request.SeasonLabel, options.FirstOrDefault()?.SeasonLabel, ""),
            actor.Division,
            actor.TeamId,
            actor.TeamName,
            date,
            startTime,
            endTime,
            string.IsNullOrWhiteSpace(fieldKey) ? null : fieldKey,
            options.Any(option => option.IsAvailable),
            options.Count,
            options);
    }

    private async Task<List<TableEntity>> LoadCandidateSlotsAsync(
        string leagueId,
        string division,
        string date,
        string? fieldKey,
        string? seasonLabel)
    {
        var query = new SlotQueryFilter
        {
            LeagueId = leagueId,
            Division = division,
            FromDate = date,
            ToDate = date,
            FieldKey = string.IsNullOrWhiteSpace(fieldKey) ? null : fieldKey,
            ExcludeCancelled = true,
            PageSize = 500,
        };

        var all = new List<TableEntity>();
        string? continuation = null;
        do
        {
            var page = await _slotRepository.QuerySlotsAsync(query, continuation);
            if (page.Items.Count > 0)
            {
                all.AddRange(page.Items);
            }

            continuation = page.ContinuationToken;
        }
        while (!string.IsNullOrWhiteSpace(continuation));

        return all
            .Where(SlotEntityUtil.IsPracticeRequestableAvailability)
            .Where(slot =>
            {
                var status = SlotEntityUtil.ReadString(slot, "Status", Constants.Status.SlotOpen);
                return !string.Equals(status, Constants.Status.SlotCancelled, StringComparison.OrdinalIgnoreCase);
            })
            .Where(slot =>
                string.IsNullOrWhiteSpace(seasonLabel) ||
                string.Equals(SlotEntityUtil.ReadString(slot, "PracticeSeasonLabel"), seasonLabel.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static PracticeAvailabilityOptionDto MapOption(TableEntity slot, IReadOnlyCollection<TableEntity> requests)
    {
        var division = SlotEntityUtil.ReadString(slot, "Division");
        var slotId = SlotEntityUtil.ReadString(slot, "SlotId", slot.RowKey);
        var activeRequests = requests
            .Where(request =>
                string.Equals((request.GetString("Division") ?? "").Trim(), division, StringComparison.OrdinalIgnoreCase) &&
                string.Equals((request.GetString("SlotId") ?? "").Trim(), slotId, StringComparison.OrdinalIgnoreCase) &&
                ActiveRequestStatuses.Contains((request.GetString("Status") ?? "").Trim(), StringComparer.OrdinalIgnoreCase))
            .ToList();

        var approvedTeamIds = activeRequests
            .Where(request => string.Equals((request.GetString("Status") ?? "").Trim(), FieldInventoryPracticeRequestStatuses.Approved, StringComparison.OrdinalIgnoreCase))
            .SelectMany(ReadReservedTeamIds)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var pendingTeamIds = activeRequests
            .Where(request => string.Equals((request.GetString("Status") ?? "").Trim(), FieldInventoryPracticeRequestStatuses.Pending, StringComparison.OrdinalIgnoreCase))
            .Select(request => (request.GetString("TeamId") ?? "").Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var pendingShareTeamIds = activeRequests
            .Where(request => string.Equals((request.GetString("Status") ?? "").Trim(), FieldInventoryPracticeRequestStatuses.Pending, StringComparison.OrdinalIgnoreCase))
            .Select(request => (request.GetString("ShareWithTeamId") ?? "").Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var bookingPolicy = SlotEntityUtil.ReadString(slot, "PracticeBookingPolicy", FieldInventoryPracticeBookingPolicies.CommissionerReview);

        return new PracticeAvailabilityOptionDto(
            slotId,
            FirstNonBlank(SlotEntityUtil.ReadString(slot, "PracticeSlotKey"), slotId),
            SlotEntityUtil.ReadString(slot, "PracticeSeasonLabel"),
            division,
            SlotEntityUtil.ReadString(slot, "GameDate"),
            WeekdayFromIso(SlotEntityUtil.ReadString(slot, "GameDate")),
            SlotEntityUtil.ReadString(slot, "StartTime"),
            SlotEntityUtil.ReadString(slot, "EndTime"),
            NormalizeFieldKey(SlotEntityUtil.ReadString(slot, "FieldKey")),
            FirstNonBlank(SlotEntityUtil.ReadString(slot, "DisplayName"), SlotEntityUtil.ReadString(slot, "FieldName")),
            bookingPolicy,
            BookingPolicyLabel(bookingPolicy),
            activeRequests.Count == 0 && string.Equals(SlotEntityUtil.ReadString(slot, "Status", Constants.Status.SlotOpen), Constants.Status.SlotOpen, StringComparison.OrdinalIgnoreCase),
            true,
            2,
            approvedTeamIds,
            pendingTeamIds,
            pendingShareTeamIds);
    }

    private async Task<ActorContext> ResolveActorAsync(string leagueId, string userId, string? requestedDivision)
    {
        if (await _membershipRepository.IsGlobalAdminAsync(userId))
        {
            return new ActorContext((requestedDivision ?? "").Trim(), "", null, true);
        }

        var membership = await _membershipRepository.GetMembershipAsync(userId, leagueId);
        var role = (membership?.GetString("Role") ?? Constants.Roles.Viewer).Trim();
        var isLeagueAdmin = string.Equals(role, Constants.Roles.LeagueAdmin, StringComparison.OrdinalIgnoreCase);
        var isCoach = string.Equals(role, Constants.Roles.Coach, StringComparison.OrdinalIgnoreCase);
        if (!isLeagueAdmin && !isCoach)
        {
            throw new ApiGuards.HttpError(403, ErrorCodes.FORBIDDEN, "Only coaches and admins can query practice availability.");
        }

        if (isLeagueAdmin)
        {
            var division = (requestedDivision ?? "").Trim();
            if (string.IsNullOrWhiteSpace(division))
            {
                throw new ApiGuards.HttpError(400, ErrorCodes.BAD_REQUEST, "division is required for admin practice availability queries.");
            }

            return new ActorContext(division, "", null, true);
        }

        var coachDivision = (membership?.GetString("Division") ?? "").Trim();
        var coachTeamId = (membership?.GetString("TeamId") ?? "").Trim();
        if (string.IsNullOrWhiteSpace(coachDivision) || string.IsNullOrWhiteSpace(coachTeamId))
        {
            throw new ApiGuards.HttpError(403, ErrorCodes.COACH_TEAM_REQUIRED, "Coach profile is missing team/division assignment.");
        }

        string? teamName = membership?.GetString("TeamName");
        if (string.IsNullOrWhiteSpace(teamName))
        {
            var team = await _teamRepository.GetTeamAsync(leagueId, coachDivision, coachTeamId);
            teamName = team?.GetString("Name");
        }

        return new ActorContext(coachDivision, coachTeamId, teamName, false);
    }

    private static IEnumerable<string> ReadReservedTeamIds(TableEntity request)
    {
        var teamId = (request.GetString("TeamId") ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(teamId))
        {
            yield return teamId;
        }

        var shareWithTeamId = (request.GetString("ShareWithTeamId") ?? "").Trim();
        if ((request.GetBoolean("OpenToShareField") ?? false) && !string.IsNullOrWhiteSpace(shareWithTeamId))
        {
            yield return shareWithTeamId;
        }
    }

    private static string RequireIsoDate(string? value, string fieldName)
    {
        var trimmed = (value ?? "").Trim();
        if (!DateOnly.TryParseExact(trimmed, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            throw new ApiGuards.HttpError(400, ErrorCodes.BAD_REQUEST, $"{fieldName} must be YYYY-MM-DD.");
        }

        return trimmed;
    }

    private static string NormalizeRequiredTime(string? value, string fieldName)
    {
        var trimmed = NormalizeTimeOrEmpty(value);
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new ApiGuards.HttpError(400, ErrorCodes.BAD_REQUEST, $"{fieldName} must be HH:MM.");
        }

        return trimmed;
    }

    private static string NormalizeTimeOrEmpty(string? value)
    {
        var trimmed = (value ?? "").Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? "" : trimmed;
    }

    private static void ValidateTimeRange(string startTime, string endTime)
    {
        if (!TimeUtil.IsValidRange(startTime, endTime, out _, out _, out var error))
        {
            throw new ApiGuards.HttpError(400, ErrorCodes.BAD_REQUEST, error);
        }
    }

    private static string NormalizeFieldKey(string? value)
        => SlotKeyUtil.NormalizeFieldKey(value ?? "");

    private static string BookingPolicyLabel(string bookingPolicy)
    {
        return (bookingPolicy ?? "").Trim().ToLowerInvariant() switch
        {
            FieldInventoryPracticeBookingPolicies.AutoApprove => "Auto-approve",
            FieldInventoryPracticeBookingPolicies.CommissionerReview => "Commissioner review",
            _ => "Not requestable",
        };
    }

    private static string WeekdayFromIso(string? isoDate)
    {
        return DateOnly.TryParse((isoDate ?? "").Trim(), out var parsed)
            ? parsed.DayOfWeek.ToString()
            : "";
    }

    private static string FirstNonBlank(params string?[] values)
    {
        foreach (var value in values)
        {
            var trimmed = (value ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(trimmed))
            {
                return trimmed;
            }
        }

        return "";
    }

    private sealed record ActorContext(string Division, string TeamId, string? TeamName, bool IsAdmin);
}
