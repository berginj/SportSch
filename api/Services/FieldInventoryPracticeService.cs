using System.Globalization;
using Azure.Data.Tables;
using GameSwap.Functions.Models;
using GameSwap.Functions.Repositories;
using GameSwap.Functions.Storage;

namespace GameSwap.Functions.Services;

public class FieldInventoryPracticeService : IFieldInventoryPracticeService
{
    private static readonly string[] ActiveRequestStatuses =
    [
        FieldInventoryPracticeRequestStatuses.Pending,
        FieldInventoryPracticeRequestStatuses.Approved,
    ];

    private readonly IFieldInventoryImportRepository _inventoryRepository;
    private readonly IMembershipRepository _membershipRepository;
    private readonly IDivisionRepository _divisionRepository;
    private readonly ITeamRepository _teamRepository;

    public FieldInventoryPracticeService(
        IFieldInventoryImportRepository inventoryRepository,
        IMembershipRepository membershipRepository,
        IDivisionRepository divisionRepository,
        ITeamRepository teamRepository)
    {
        _inventoryRepository = inventoryRepository;
        _membershipRepository = membershipRepository;
        _divisionRepository = divisionRepository;
        _teamRepository = teamRepository;
    }

    public async Task<FieldInventoryPracticeAdminResponse> GetAdminViewAsync(string? seasonLabel, CorrelationContext context)
    {
        var bundle = await LoadBundleAsync(context.LeagueId, seasonLabel);
        return BuildAdminResponse(bundle);
    }

    public async Task<FieldInventoryPracticeCoachResponse> GetCoachViewAsync(string? seasonLabel, string userId, CorrelationContext context)
    {
        var membership = await RequireCoachMembershipAsync(context.LeagueId, userId);
        var bundle = await LoadBundleAsync(context.LeagueId, seasonLabel);
        return BuildCoachResponse(bundle, membership.TeamDivision, membership.TeamId, membership.TeamName);
    }

    public async Task<FieldInventoryPracticeAdminResponse> SaveDivisionAliasAsync(FieldInventoryDivisionAliasSaveRequest request, string userId, CorrelationContext context)
    {
        var rawDivisionName = (request.RawDivisionName ?? "").Trim();
        var canonicalDivisionCode = (request.CanonicalDivisionCode ?? "").Trim();
        if (string.IsNullOrWhiteSpace(rawDivisionName) || string.IsNullOrWhiteSpace(canonicalDivisionCode))
        {
            throw new ApiGuards.HttpError(400, ErrorCodes.BAD_REQUEST, "rawDivisionName and canonicalDivisionCode are required.");
        }

        var divisions = await _divisionRepository.QueryDivisionsAsync(context.LeagueId);
        var division = divisions.FirstOrDefault(d => string.Equals(d.GetString("Code") ?? d.RowKey, canonicalDivisionCode, StringComparison.OrdinalIgnoreCase));
        if (division is null)
        {
            throw new ApiGuards.HttpError(404, ErrorCodes.DIVISION_NOT_FOUND, "Division not found.");
        }

        var now = DateTimeOffset.UtcNow;
        var normalized = NormalizeLookupKey(rawDivisionName);
        await _inventoryRepository.UpsertDivisionAliasAsync(new FieldInventoryDivisionAliasEntity
        {
            Id = normalized,
            LeagueId = context.LeagueId,
            RawDivisionName = rawDivisionName,
            NormalizedLookupKey = normalized,
            CanonicalDivisionCode = canonicalDivisionCode,
            CanonicalDivisionName = division.GetString("Name") ?? canonicalDivisionCode,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = userId,
        });

        return await GetAdminViewAsync(null, context);
    }

    public async Task<FieldInventoryPracticeAdminResponse> SaveTeamAliasAsync(FieldInventoryTeamAliasSaveRequest request, string userId, CorrelationContext context)
    {
        var rawTeamName = (request.RawTeamName ?? "").Trim();
        var divisionCode = (request.CanonicalDivisionCode ?? "").Trim();
        var canonicalTeamId = (request.CanonicalTeamId ?? "").Trim();
        if (string.IsNullOrWhiteSpace(rawTeamName) || string.IsNullOrWhiteSpace(divisionCode) || string.IsNullOrWhiteSpace(canonicalTeamId))
        {
            throw new ApiGuards.HttpError(400, ErrorCodes.BAD_REQUEST, "rawTeamName, canonicalDivisionCode, and canonicalTeamId are required.");
        }

        var team = await _teamRepository.GetTeamAsync(context.LeagueId, divisionCode, canonicalTeamId);
        if (team is null)
        {
            throw new ApiGuards.HttpError(404, ErrorCodes.TEAM_NOT_FOUND, "Team not found.");
        }

        var now = DateTimeOffset.UtcNow;
        var normalized = NormalizeLookupKey(rawTeamName);
        var id = $"{divisionCode}|{normalized}";
        await _inventoryRepository.UpsertTeamAliasAsync(new FieldInventoryTeamAliasEntity
        {
            Id = id,
            LeagueId = context.LeagueId,
            RawTeamName = rawTeamName,
            NormalizedLookupKey = normalized,
            CanonicalDivisionCode = divisionCode,
            CanonicalTeamId = canonicalTeamId,
            CanonicalTeamName = request.CanonicalTeamName?.Trim() ?? team.GetString("Name") ?? canonicalTeamId,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = userId,
        });

        return await GetAdminViewAsync(null, context);
    }

    public async Task<FieldInventoryPracticeAdminResponse> SaveGroupPolicyAsync(FieldInventoryGroupPolicySaveRequest request, string userId, CorrelationContext context)
    {
        var rawGroupName = (request.RawGroupName ?? "").Trim();
        var bookingPolicy = NormalizeBookingPolicy(request.BookingPolicy);
        if (string.IsNullOrWhiteSpace(rawGroupName))
        {
            throw new ApiGuards.HttpError(400, ErrorCodes.BAD_REQUEST, "rawGroupName is required.");
        }

        var now = DateTimeOffset.UtcNow;
        var normalized = NormalizeLookupKey(rawGroupName);
        await _inventoryRepository.UpsertGroupPolicyAsync(new FieldInventoryGroupPolicyEntity
        {
            Id = normalized,
            LeagueId = context.LeagueId,
            RawGroupName = rawGroupName,
            NormalizedLookupKey = normalized,
            BookingPolicy = bookingPolicy,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = userId,
        });

        return await GetAdminViewAsync(null, context);
    }

    public async Task<FieldInventoryPracticeCoachResponse> CreatePracticeRequestAsync(FieldInventoryPracticeRequestCreateRequest request, string userId, CorrelationContext context)
    {
        var actor = await RequireCoachMembershipAsync(context.LeagueId, userId);
        var bundle = await LoadBundleAsync(context.LeagueId, request.SeasonLabel);
        var teamId = actor.IsLeagueAdminOrGlobalAdmin
            ? (request.TeamId ?? "").Trim() is var requestedTeamId && !string.IsNullOrWhiteSpace(requestedTeamId) ? requestedTeamId : actor.TeamId
            : actor.TeamId;
        var teamName = actor.TeamName;
        if (string.IsNullOrWhiteSpace(teamId))
        {
            throw new ApiGuards.HttpError(403, ErrorCodes.COACH_TEAM_REQUIRED, "Your coach profile needs a team assignment before requesting practice space.");
        }

        var slotKey = (request.PracticeSlotKey ?? "").Trim();
        var slot = bundle.RequestableSlots.FirstOrDefault(s => string.Equals(s.PracticeSlotKey, slotKey, StringComparison.OrdinalIgnoreCase));
        if (slot is null)
        {
            throw new ApiGuards.HttpError(404, ErrorCodes.PRACTICE_SPACE_NOT_FOUND, "Practice space not found.");
        }

        if (slot.BookingPolicy == FieldInventoryPracticeBookingPolicies.NotRequestable)
        {
            throw new ApiGuards.HttpError(409, ErrorCodes.PRACTICE_SPACE_NOT_REQUESTABLE, "This field space is not requestable through SportsCH.");
        }

        if (slot.RemainingCapacity <= 0)
        {
            throw new ApiGuards.HttpError(409, ErrorCodes.PRACTICE_SPACE_FULL, "This practice space is already full.");
        }

        var duplicate = bundle.Requests.Any(r =>
            string.Equals(r.PracticeSlotKey, slot.PracticeSlotKey, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(r.TeamId, teamId, StringComparison.OrdinalIgnoreCase) &&
            ActiveRequestStatuses.Contains(r.Status, StringComparer.OrdinalIgnoreCase));
        if (duplicate)
        {
            throw new ApiGuards.HttpError(409, ErrorCodes.ALREADY_EXISTS, "Your team already has an active request for this practice space.");
        }

        var activeForTeam = bundle.Requests.Count(r =>
            string.Equals(r.TeamId, teamId, StringComparison.OrdinalIgnoreCase) &&
            ActiveRequestStatuses.Contains(r.Status, StringComparer.OrdinalIgnoreCase));
        if (activeForTeam >= 3)
        {
            throw new ApiGuards.HttpError(409, ErrorCodes.CONFLICT, "Your team already has 3 active practice-space requests.");
        }

        var status = slot.BookingPolicy == FieldInventoryPracticeBookingPolicies.AutoApprove
            ? FieldInventoryPracticeRequestStatuses.Approved
            : FieldInventoryPracticeRequestStatuses.Pending;
        var now = DateTimeOffset.UtcNow;
        var entity = new FieldInventoryPracticeRequestEntity
        {
            Id = Guid.NewGuid().ToString("N"),
            LeagueId = context.LeagueId,
            SeasonLabel = bundle.SeasonLabel,
            PracticeSlotKey = slot.PracticeSlotKey,
            LiveRecordId = slot.LiveRecordId,
            Date = slot.Date,
            DayOfWeek = slot.DayOfWeek,
            StartTime = slot.StartTime,
            EndTime = slot.EndTime,
            FieldId = slot.FieldId,
            FieldName = slot.FieldName,
            TeamId = teamId,
            TeamName = teamName,
            BookingPolicy = slot.BookingPolicy,
            Status = status,
            Notes = (request.Notes ?? "").Trim(),
            CreatedBy = userId,
            CreatedAt = now,
            ReviewedBy = status == FieldInventoryPracticeRequestStatuses.Approved ? userId : null,
            ReviewedAt = status == FieldInventoryPracticeRequestStatuses.Approved ? now : null,
            ReviewReason = status == FieldInventoryPracticeRequestStatuses.Approved ? "Auto-approved from Ponytail-assigned field space." : null,
            UpdatedAt = now,
        };

        await _inventoryRepository.UpsertPracticeRequestAsync(entity);
        return await GetCoachViewAsync(bundle.SeasonLabel, userId, context);
    }

    public async Task<FieldInventoryPracticeAdminResponse> ApprovePracticeRequestAsync(string requestId, FieldInventoryPracticeRequestDecisionRequest request, string userId, CorrelationContext context)
    {
        var bundle = await LoadBundleAsync(context.LeagueId, null);
        var entity = bundle.RequestEntities.FirstOrDefault(r => string.Equals(r.Id, requestId, StringComparison.OrdinalIgnoreCase));
        if (entity is null)
        {
            throw new ApiGuards.HttpError(404, ErrorCodes.PRACTICE_REQUEST_NOT_FOUND, "Practice request not found.");
        }

        var activeCount = bundle.RequestEntities.Count(r =>
            string.Equals(r.PracticeSlotKey, entity.PracticeSlotKey, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(r.Status, FieldInventoryPracticeRequestStatuses.Approved, StringComparison.OrdinalIgnoreCase));
        if (!string.Equals(entity.Status, FieldInventoryPracticeRequestStatuses.Approved, StringComparison.OrdinalIgnoreCase) && activeCount >= 2)
        {
            throw new ApiGuards.HttpError(409, ErrorCodes.PRACTICE_SPACE_FULL, "This practice space already has 2 approved teams.");
        }

        entity.Status = FieldInventoryPracticeRequestStatuses.Approved;
        entity.ReviewReason = (request.Reason ?? "").Trim();
        entity.ReviewedBy = userId;
        entity.ReviewedAt = DateTimeOffset.UtcNow;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await _inventoryRepository.UpsertPracticeRequestAsync(entity);
        return await GetAdminViewAsync(entity.SeasonLabel, context);
    }

    public async Task<FieldInventoryPracticeAdminResponse> RejectPracticeRequestAsync(string requestId, FieldInventoryPracticeRequestDecisionRequest request, string userId, CorrelationContext context)
    {
        var bundle = await LoadBundleAsync(context.LeagueId, null);
        var entity = bundle.RequestEntities.FirstOrDefault(r => string.Equals(r.Id, requestId, StringComparison.OrdinalIgnoreCase));
        if (entity is null)
        {
            throw new ApiGuards.HttpError(404, ErrorCodes.PRACTICE_REQUEST_NOT_FOUND, "Practice request not found.");
        }

        entity.Status = FieldInventoryPracticeRequestStatuses.Rejected;
        entity.ReviewReason = (request.Reason ?? "").Trim();
        entity.ReviewedBy = userId;
        entity.ReviewedAt = DateTimeOffset.UtcNow;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await _inventoryRepository.UpsertPracticeRequestAsync(entity);
        return await GetAdminViewAsync(entity.SeasonLabel, context);
    }

    public async Task<FieldInventoryPracticeCoachResponse> CancelPracticeRequestAsync(string requestId, string userId, CorrelationContext context)
    {
        var actor = await RequireCoachMembershipAsync(context.LeagueId, userId);
        var bundle = await LoadBundleAsync(context.LeagueId, null);
        var entity = bundle.RequestEntities.FirstOrDefault(r => string.Equals(r.Id, requestId, StringComparison.OrdinalIgnoreCase));
        if (entity is null)
        {
            throw new ApiGuards.HttpError(404, ErrorCodes.PRACTICE_REQUEST_NOT_FOUND, "Practice request not found.");
        }

        if (!actor.IsLeagueAdminOrGlobalAdmin && !string.Equals(entity.TeamId, actor.TeamId, StringComparison.OrdinalIgnoreCase))
        {
            throw new ApiGuards.HttpError(403, ErrorCodes.FORBIDDEN, "You can only cancel your own team's practice requests.");
        }

        entity.Status = FieldInventoryPracticeRequestStatuses.Cancelled;
        entity.ReviewReason = "Cancelled";
        entity.ReviewedBy = userId;
        entity.ReviewedAt = DateTimeOffset.UtcNow;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await _inventoryRepository.UpsertPracticeRequestAsync(entity);
        return await GetCoachViewAsync(entity.SeasonLabel, userId, context);
    }

    private async Task<Bundle> LoadBundleAsync(string leagueId, string? seasonLabel)
    {
        var commitRuns = await _inventoryRepository.GetCommitRunsAsync(leagueId);
        var seasons = commitRuns
            .Select(x => x.SeasonLabel)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var resolvedSeason = ResolveSeasonLabel(seasonLabel, seasons);
        var liveRecords = string.IsNullOrWhiteSpace(resolvedSeason)
            ? new List<FieldInventoryLiveRecordEntity>()
            : await _inventoryRepository.GetLiveRecordsAsync(leagueId, resolvedSeason);

        var divisions = await _divisionRepository.QueryDivisionsAsync(leagueId);
        var teams = await _teamRepository.QueryAllTeamsAsync(leagueId);
        var divisionAliases = await _inventoryRepository.GetDivisionAliasesAsync(leagueId);
        var teamAliases = await _inventoryRepository.GetTeamAliasesAsync(leagueId);
        var groupPolicies = await _inventoryRepository.GetGroupPoliciesAsync(leagueId);
        var requests = string.IsNullOrWhiteSpace(resolvedSeason)
            ? new List<FieldInventoryPracticeRequestEntity>()
            : await _inventoryRepository.GetPracticeRequestsAsync(leagueId, resolvedSeason);

        var alignment = BuildAlignmentRows(resolvedSeason, liveRecords, divisions, teams, divisionAliases, teamAliases, groupPolicies, requests);
        return new Bundle
        {
            LeagueId = leagueId,
            SeasonLabel = resolvedSeason,
            SeasonOptions = seasons.Select((label, index) => new FieldInventoryPracticeSeasonOptionDto(label, index == 0 && string.IsNullOrWhiteSpace(seasonLabel) || string.Equals(label, resolvedSeason, StringComparison.OrdinalIgnoreCase))).ToList(),
            LiveRecords = liveRecords,
            RequestEntities = requests,
            Requests = alignment.Requests,
            AdminRows = alignment.Rows,
            RequestableSlots = alignment.Slots,
            CanonicalFields = BuildCanonicalFieldOptions(liveRecords),
            CanonicalDivisions = divisions
                .Select(d => new CanonicalDivisionOptionDto(d.GetString("Code") ?? d.RowKey, d.GetString("Name") ?? d.GetString("Code") ?? d.RowKey))
                .OrderBy(d => d.Code, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            CanonicalTeams = teams
                .Select(t => new CanonicalTeamOptionDto(t.GetString("Division") ?? "", t.RowKey, t.GetString("Name") ?? t.RowKey))
                .OrderBy(t => t.DivisionCode, StringComparer.OrdinalIgnoreCase)
                .ThenBy(t => t.TeamName, StringComparer.OrdinalIgnoreCase)
                .ToList(),
        };
    }

    private static string ResolveSeasonLabel(string? requestedSeason, List<string> seasons)
    {
        var season = (requestedSeason ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(season)) return season;
        return seasons.FirstOrDefault() ?? "";
    }

    private static AlignmentResult BuildAlignmentRows(
        string seasonLabel,
        List<FieldInventoryLiveRecordEntity> liveRecords,
        List<TableEntity> divisions,
        List<TableEntity> teams,
        List<FieldInventoryDivisionAliasEntity> divisionAliases,
        List<FieldInventoryTeamAliasEntity> teamAliases,
        List<FieldInventoryGroupPolicyEntity> groupPolicies,
        List<FieldInventoryPracticeRequestEntity> requests)
    {
        var divisionOptions = divisions
            .Select(d => new DivisionOption(d.GetString("Code") ?? d.RowKey, d.GetString("Name") ?? d.GetString("Code") ?? d.RowKey))
            .ToList();
        var teamOptions = teams
            .Select(t => new TeamOption(t.GetString("Division") ?? "", t.RowKey, t.GetString("Name") ?? t.RowKey))
            .ToList();
        var requestDtos = requests.Select(MapRequest).ToList();
        var rows = new List<FieldInventoryPracticeAdminRowDto>();
        var slots = new List<FieldInventoryPracticeSlotDto>();

        foreach (var record in liveRecords.OrderBy(r => r.Date).ThenBy(r => r.StartTime).ThenBy(r => r.FieldName))
        {
            var divisionMatch = ResolveDivision(record.AssignedDivision, divisionOptions, divisionAliases);
            var teamMatch = ResolveTeam(record.AssignedTeamOrEvent, divisionMatch.Code, teamOptions, teamAliases);
            var policy = ResolvePolicy(record, groupPolicies);
            var recordBlocks = BuildPracticeSlots(seasonLabel, record, policy, requests);
            slots.AddRange(recordBlocks);
            var approvedCount = recordBlocks.Sum(x => x.ApprovedCount);
            var pendingCount = recordBlocks.Sum(x => x.PendingCount);

            var issues = new List<string>();
            if (string.IsNullOrWhiteSpace(record.FieldId)) issues.Add("field_unmapped");
            if (!string.IsNullOrWhiteSpace(record.AssignedDivision) && string.IsNullOrWhiteSpace(divisionMatch.Code)) issues.Add("division_unmapped");
            if (!string.IsNullOrWhiteSpace(record.AssignedTeamOrEvent) && string.IsNullOrWhiteSpace(teamMatch.TeamId)) issues.Add("team_unmapped");
            if (policy.BookingPolicy == FieldInventoryPracticeBookingPolicies.NotRequestable && policy.IsActionable) issues.Add("policy_unmapped");

            rows.Add(new FieldInventoryPracticeAdminRowDto(
                record.Id,
                seasonLabel,
                record.Date,
                record.DayOfWeek,
                record.StartTime,
                record.EndTime,
                record.SlotDurationMinutes,
                record.AvailabilityStatus,
                record.UtilizationStatus,
                record.UsageType,
                record.UsedBy,
                record.FieldId,
                record.FieldName,
                record.RawFieldName,
                record.AssignedGroup,
                record.AssignedDivision,
                record.AssignedTeamOrEvent,
                divisionMatch.Code,
                divisionMatch.Name,
                teamMatch.TeamId,
                teamMatch.TeamName,
                policy.BookingPolicy,
                policy.Reason,
                recordBlocks.Count,
                approvedCount,
                pendingCount,
                issues));
        }

        return new AlignmentResult(rows, slots.OrderBy(s => s.Date).ThenBy(s => s.StartTime).ThenBy(s => s.FieldName).ToList(), requestDtos);
    }

    private static List<CanonicalFieldOptionDto> BuildCanonicalFieldOptions(List<FieldInventoryLiveRecordEntity> liveRecords)
    {
        return liveRecords
            .Where(r => !string.IsNullOrWhiteSpace(r.FieldId))
            .GroupBy(r => r.FieldId, StringComparer.OrdinalIgnoreCase)
            .Select(g => new CanonicalFieldOptionDto(g.Key, g.First().FieldName, g.First().FieldName, ""))
            .OrderBy(x => x.CanonicalFieldName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<FieldInventoryPracticeSlotDto> BuildPracticeSlots(
        string seasonLabel,
        FieldInventoryLiveRecordEntity record,
        PolicyDecision policy,
        List<FieldInventoryPracticeRequestEntity> requests)
    {
        var results = new List<FieldInventoryPracticeSlotDto>();
        if (policy.BookingPolicy == FieldInventoryPracticeBookingPolicies.NotRequestable) return results;

        if (!string.Equals(record.AvailabilityStatus, FieldInventoryAvailabilityStatuses.Available, StringComparison.OrdinalIgnoreCase)) return results;
        if (!string.Equals(record.UtilizationStatus, FieldInventoryUtilizationStatuses.NotUsed, StringComparison.OrdinalIgnoreCase)) return results;

        if (!TryParseMinutes(record.StartTime, out var startMinutes) || !TryParseMinutes(record.EndTime, out var endMinutes)) return results;
        if (endMinutes - startMinutes < 90) return results;

        for (var blockStart = startMinutes; blockStart + 90 <= endMinutes; blockStart += 90)
        {
            var blockEnd = blockStart + 90;
            var slotKey = $"{record.Id}|{FormatMinutes(blockStart)}|{FormatMinutes(blockEnd)}";
            var slotRequests = requests.Where(r =>
                string.Equals(r.PracticeSlotKey, slotKey, StringComparison.OrdinalIgnoreCase) &&
                ActiveRequestStatuses.Contains(r.Status, StringComparer.OrdinalIgnoreCase)).ToList();
            var approvedTeamIds = slotRequests
                .Where(r => string.Equals(r.Status, FieldInventoryPracticeRequestStatuses.Approved, StringComparison.OrdinalIgnoreCase))
                .Select(r => r.TeamId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var pendingTeamIds = slotRequests
                .Where(r => string.Equals(r.Status, FieldInventoryPracticeRequestStatuses.Pending, StringComparison.OrdinalIgnoreCase))
                .Select(r => r.TeamId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var reservedCount = approvedTeamIds.Count + pendingTeamIds.Count;
            results.Add(new FieldInventoryPracticeSlotDto(
                slotKey,
                seasonLabel,
                record.Id,
                record.Date,
                record.DayOfWeek,
                FormatMinutes(blockStart),
                FormatMinutes(blockEnd),
                90,
                record.FieldId,
                record.FieldName,
                policy.BookingPolicy,
                BookingPolicyLabel(policy.BookingPolicy),
                policy.Reason,
                record.AssignedGroup,
                record.AssignedDivision,
                record.AssignedTeamOrEvent,
                2,
                approvedTeamIds.Count,
                pendingTeamIds.Count,
                Math.Max(0, 2 - reservedCount),
                approvedTeamIds,
                pendingTeamIds));
        }

        return results;
    }

    private static PolicyDecision ResolvePolicy(FieldInventoryLiveRecordEntity record, List<FieldInventoryGroupPolicyEntity> groupPolicies)
    {
        var availabilityAvailable = string.Equals(record.AvailabilityStatus, FieldInventoryAvailabilityStatuses.Available, StringComparison.OrdinalIgnoreCase);
        var notUsed = string.Equals(record.UtilizationStatus, FieldInventoryUtilizationStatuses.NotUsed, StringComparison.OrdinalIgnoreCase);
        if (!availabilityAvailable || !notUsed)
        {
            return new PolicyDecision(FieldInventoryPracticeBookingPolicies.NotRequestable, "Used or unavailable inventory.", false);
        }

        var rawGroup = (record.AssignedGroup ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(rawGroup))
        {
            var normalizedGroup = NormalizeLookupKey(rawGroup);
            var saved = groupPolicies.FirstOrDefault(x => string.Equals(x.NormalizedLookupKey, normalizedGroup, StringComparison.OrdinalIgnoreCase));
            if (saved is not null)
            {
                return new PolicyDecision(saved.BookingPolicy, $"Mapped from group '{rawGroup}'.", true);
            }
        }

        if (HasPonytailSignal(record))
        {
            return new PolicyDecision(FieldInventoryPracticeBookingPolicies.AutoApprove, "Ponytail-assigned space auto-approves coach requests.", true);
        }

        if (!string.IsNullOrWhiteSpace(rawGroup))
        {
            var normalizedGroup = NormalizeLookupKey(rawGroup);
            if (normalizedGroup.Contains("ponytail", StringComparison.OrdinalIgnoreCase))
            {
                return new PolicyDecision(FieldInventoryPracticeBookingPolicies.AutoApprove, "Ponytail-assigned space auto-approves coach requests.", true);
            }
        }

        if (string.IsNullOrWhiteSpace(record.AssignedGroup)
            && string.IsNullOrWhiteSpace(record.AssignedDivision)
            && string.IsNullOrWhiteSpace(record.AssignedTeamOrEvent))
        {
            return new PolicyDecision(FieldInventoryPracticeBookingPolicies.CommissionerReview, "Unassigned available space requires commissioner approval.", false);
        }

        return new PolicyDecision(FieldInventoryPracticeBookingPolicies.NotRequestable, "Needs policy mapping before coaches can request it.", true);
    }

    private static bool HasPonytailSignal(FieldInventoryLiveRecordEntity record)
    {
        var signals = new[]
        {
            NormalizeLookupKey(record.AssignedGroup ?? ""),
            NormalizeLookupKey(record.AssignedDivision ?? ""),
            NormalizeLookupKey(record.AssignedTeamOrEvent ?? ""),
        };

        return signals.Any(signal =>
            !string.IsNullOrWhiteSpace(signal) &&
            (signal.Contains("ponytail", StringComparison.OrdinalIgnoreCase)
                || signal.Contains("ponytails", StringComparison.OrdinalIgnoreCase)
                || signal.Contains("pypractice", StringComparison.OrdinalIgnoreCase)
                || signal == "py"));
    }

    private static DivisionMatch ResolveDivision(string? rawDivision, List<DivisionOption> divisions, List<FieldInventoryDivisionAliasEntity> aliases)
    {
        var raw = (rawDivision ?? "").Trim();
        if (string.IsNullOrWhiteSpace(raw)) return new DivisionMatch(null, null);

        var normalized = NormalizeLookupKey(raw);
        var alias = aliases.FirstOrDefault(x => string.Equals(x.NormalizedLookupKey, normalized, StringComparison.OrdinalIgnoreCase));
        if (alias is not null)
        {
            return new DivisionMatch(alias.CanonicalDivisionCode, alias.CanonicalDivisionName);
        }

        var exact = divisions.FirstOrDefault(d =>
            string.Equals(d.Code, raw, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(d.Name, raw, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(NormalizeLookupKey(d.Name), normalized, StringComparison.OrdinalIgnoreCase));
        return exact is null ? new DivisionMatch(null, null) : new DivisionMatch(exact.Code, exact.Name);
    }

    private static TeamMatch ResolveTeam(string? rawTeam, string? divisionCode, List<TeamOption> teams, List<FieldInventoryTeamAliasEntity> aliases)
    {
        var raw = (rawTeam ?? "").Trim();
        if (string.IsNullOrWhiteSpace(raw)) return new TeamMatch(null, null);

        var normalized = NormalizeLookupKey(raw);
        var scopedAlias = aliases.FirstOrDefault(x =>
            string.Equals(x.CanonicalDivisionCode, divisionCode ?? "", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.NormalizedLookupKey, normalized, StringComparison.OrdinalIgnoreCase));
        if (scopedAlias is not null)
        {
            return new TeamMatch(scopedAlias.CanonicalTeamId, scopedAlias.CanonicalTeamName);
        }

        var candidates = teams.Where(t =>
            string.IsNullOrWhiteSpace(divisionCode) || string.Equals(t.DivisionCode, divisionCode, StringComparison.OrdinalIgnoreCase)).ToList();
        var exact = candidates.FirstOrDefault(t =>
            string.Equals(t.TeamId, raw, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(t.TeamName, raw, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(NormalizeLookupKey(t.TeamName), normalized, StringComparison.OrdinalIgnoreCase));
        return exact is null ? new TeamMatch(null, null) : new TeamMatch(exact.TeamId, exact.TeamName);
    }

    private static FieldInventoryPracticeRequestDto MapRequest(FieldInventoryPracticeRequestEntity request)
        => new(
            request.Id,
            request.SeasonLabel,
            request.PracticeSlotKey,
            request.LiveRecordId,
            request.Date,
            request.DayOfWeek,
            request.StartTime,
            request.EndTime,
            request.FieldId,
            request.FieldName,
            request.TeamId,
            request.TeamName,
            request.Status,
            request.BookingPolicy,
            BookingPolicyLabel(request.BookingPolicy),
            request.Notes,
            request.CreatedBy,
            request.CreatedAt,
            request.ReviewedBy,
            request.ReviewedAt,
            request.ReviewReason);

    private static FieldInventoryPracticeAdminResponse BuildAdminResponse(Bundle bundle)
    {
        var summary = BuildSummary(bundle.AdminRows, bundle.Requests);
        return new FieldInventoryPracticeAdminResponse(
            bundle.SeasonLabel,
            bundle.SeasonOptions,
            summary,
            bundle.AdminRows,
            bundle.Requests,
            bundle.CanonicalFields,
            bundle.CanonicalDivisions,
            bundle.CanonicalTeams);
    }

    private static FieldInventoryPracticeCoachResponse BuildCoachResponse(Bundle bundle, string division, string teamId, string? teamName)
    {
        var requests = bundle.Requests
            .Where(r => string.Equals(r.TeamId, teamId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(r => r.Date)
            .ThenByDescending(r => r.StartTime)
            .ToList();
        var slots = bundle.RequestableSlots
            .Where(s => s.RemainingCapacity > 0 || requests.Any(r => string.Equals(r.PracticeSlotKey, s.PracticeSlotKey, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(s => s.Date)
            .ThenBy(s => s.StartTime)
            .ThenBy(s => s.FieldName)
            .ToList();
        return new FieldInventoryPracticeCoachResponse(
            bundle.SeasonLabel,
            bundle.SeasonOptions,
            division,
            teamId,
            teamName,
            BuildSummary(bundle.AdminRows, bundle.Requests),
            slots,
            requests);
    }

    private static FieldInventoryPracticeSummaryDto BuildSummary(List<FieldInventoryPracticeAdminRowDto> rows, List<FieldInventoryPracticeRequestDto> requests)
    {
        return new FieldInventoryPracticeSummaryDto(
            rows.Count,
            rows.Sum(r => r.RequestableBlockCount),
            rows.Where(r => r.BookingPolicy == FieldInventoryPracticeBookingPolicies.AutoApprove).Sum(r => r.RequestableBlockCount),
            rows.Where(r => r.BookingPolicy == FieldInventoryPracticeBookingPolicies.CommissionerReview).Sum(r => r.RequestableBlockCount),
            requests.Count(r => string.Equals(r.Status, FieldInventoryPracticeRequestStatuses.Pending, StringComparison.OrdinalIgnoreCase)),
            requests.Count(r => string.Equals(r.Status, FieldInventoryPracticeRequestStatuses.Approved, StringComparison.OrdinalIgnoreCase)),
            rows.Count(r => r.MappingIssues.Contains("division_unmapped")),
            rows.Count(r => r.MappingIssues.Contains("team_unmapped")),
            rows.Count(r => r.MappingIssues.Contains("policy_unmapped")));
    }

    private async Task<MembershipContext> RequireCoachMembershipAsync(string leagueId, string userId)
    {
        var membership = await _membershipRepository.GetMembershipAsync(userId, leagueId);
        var role = (membership?.GetString("Role") ?? "").Trim();
        var isGlobalAdmin = await _membershipRepository.IsGlobalAdminAsync(userId);
        var isLeagueAdmin = string.Equals(role, Constants.Roles.LeagueAdmin, StringComparison.OrdinalIgnoreCase);
        var isCoach = string.Equals(role, Constants.Roles.Coach, StringComparison.OrdinalIgnoreCase);
        if (!isCoach && !isLeagueAdmin && !isGlobalAdmin)
        {
            throw new ApiGuards.HttpError(403, ErrorCodes.FORBIDDEN, "Only coaches and admins can use practice space requests.");
        }

        var division = ReadMembershipValue(membership, "Division", "CoachDivision");
        var teamId = ReadMembershipValue(membership, "TeamId", "CoachTeamId");
        var teamName = membership?.GetString("TeamName");
        return new MembershipContext(role, isLeagueAdmin || isGlobalAdmin, division, teamId, teamName);
    }

    private static string ReadMembershipValue(TableEntity? membership, string primary, string legacy)
    {
        return (membership?.GetString(primary) ?? membership?.GetString(legacy) ?? "").Trim();
    }

    private static string NormalizeLookupKey(string value)
    {
        return string.Concat((value ?? "")
            .Trim()
            .ToLowerInvariant()
            .Where(ch => char.IsLetterOrDigit(ch)));
    }

    private static string NormalizeBookingPolicy(string? value)
    {
        var normalized = (value ?? "").Trim().ToLowerInvariant();
        return normalized switch
        {
            FieldInventoryPracticeBookingPolicies.AutoApprove => FieldInventoryPracticeBookingPolicies.AutoApprove,
            FieldInventoryPracticeBookingPolicies.CommissionerReview => FieldInventoryPracticeBookingPolicies.CommissionerReview,
            _ => FieldInventoryPracticeBookingPolicies.NotRequestable,
        };
    }

    private static string BookingPolicyLabel(string bookingPolicy)
        => bookingPolicy switch
        {
            FieldInventoryPracticeBookingPolicies.AutoApprove => "Auto-approve",
            FieldInventoryPracticeBookingPolicies.CommissionerReview => "Commissioner review",
            _ => "Not requestable",
        };

    private static bool TryParseMinutes(string? value, out int minutes)
    {
        minutes = 0;
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (!TimeOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            && !TimeOnly.TryParse(value, out parsed))
        {
            return false;
        }
        minutes = parsed.Hour * 60 + parsed.Minute;
        return true;
    }

    private static string FormatMinutes(int minutes)
    {
        var clamped = Math.Max(0, minutes);
        return $"{clamped / 60:D2}:{clamped % 60:D2}";
    }

    private sealed class Bundle
    {
        public string LeagueId { get; set; } = "";
        public string SeasonLabel { get; set; } = "";
        public List<FieldInventoryPracticeSeasonOptionDto> SeasonOptions { get; set; } = [];
        public List<FieldInventoryLiveRecordEntity> LiveRecords { get; set; } = [];
        public List<FieldInventoryPracticeRequestEntity> RequestEntities { get; set; } = [];
        public List<FieldInventoryPracticeRequestDto> Requests { get; set; } = [];
        public List<FieldInventoryPracticeAdminRowDto> AdminRows { get; set; } = [];
        public List<FieldInventoryPracticeSlotDto> RequestableSlots { get; set; } = [];
        public List<CanonicalFieldOptionDto> CanonicalFields { get; set; } = [];
        public List<CanonicalDivisionOptionDto> CanonicalDivisions { get; set; } = [];
        public List<CanonicalTeamOptionDto> CanonicalTeams { get; set; } = [];
    }

    private sealed record DivisionOption(string Code, string Name);
    private sealed record TeamOption(string DivisionCode, string TeamId, string TeamName);
    private sealed record DivisionMatch(string? Code, string? Name);
    private sealed record TeamMatch(string? TeamId, string? TeamName);
    private sealed record PolicyDecision(string BookingPolicy, string Reason, bool IsActionable);
    private sealed record AlignmentResult(List<FieldInventoryPracticeAdminRowDto> Rows, List<FieldInventoryPracticeSlotDto> Slots, List<FieldInventoryPracticeRequestDto> Requests);
    private sealed record MembershipContext(string Role, bool IsLeagueAdminOrGlobalAdmin, string TeamDivision, string TeamId, string? TeamName);
}
