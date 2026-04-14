using System.Globalization;
using Azure;
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
    private readonly ISlotRepository _slotRepository;
    private readonly IPracticeRequestRepository _practiceRequestRepository;
    private readonly IPracticeRequestService _practiceRequestService;

    public FieldInventoryPracticeService(
        IFieldInventoryImportRepository inventoryRepository,
        IMembershipRepository membershipRepository,
        IDivisionRepository divisionRepository,
        ITeamRepository teamRepository,
        ISlotRepository slotRepository,
        IPracticeRequestRepository practiceRequestRepository,
        IPracticeRequestService practiceRequestService)
    {
        _inventoryRepository = inventoryRepository;
        _membershipRepository = membershipRepository;
        _divisionRepository = divisionRepository;
        _teamRepository = teamRepository;
        _slotRepository = slotRepository;
        _practiceRequestRepository = practiceRequestRepository;
        _practiceRequestService = practiceRequestService;
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

    public async Task<FieldInventoryPracticeNormalizeResponse> NormalizeAvailabilityAsync(FieldInventoryPracticeNormalizeRequest request, string userId, CorrelationContext context)
    {
        var bundle = await LoadBundleAsync(context.LeagueId, request.SeasonLabel);
        var filteredBlocks = FilterBlocks(bundle.Blocks, request.DateFrom, request.DateTo, request.FieldId);

        var createdBlocks = 0;
        var updatedBlocks = 0;
        var alreadyNormalizedBlocks = 0;
        var conflictBlocks = 0;
        var blockedBlocks = 0;

        foreach (var block in filteredBlocks)
        {
            switch (block.NormalizationState)
            {
                case "ready":
                    if (!request.DryRun)
                    {
                        await EnsureCanonicalSlotAsync(block, userId);
                    }
                    createdBlocks++;
                    break;
                case "normalized":
                    if (block.NeedsSlotMetadataSync)
                    {
                        if (!request.DryRun)
                        {
                            await EnsureCanonicalSlotAsync(block, userId);
                        }
                        updatedBlocks++;
                    }
                    else
                    {
                        alreadyNormalizedBlocks++;
                    }
                    break;
                case "conflict":
                    conflictBlocks++;
                    break;
                default:
                    blockedBlocks++;
                    break;
            }
        }

        var adminView = request.DryRun
            ? BuildAdminResponse(bundle)
            : await GetAdminViewAsync(bundle.SeasonLabel, context);

        return new FieldInventoryPracticeNormalizeResponse(
            new FieldInventoryPracticeNormalizeResultDto(
                filteredBlocks.Count,
                createdBlocks,
                updatedBlocks,
                alreadyNormalizedBlocks,
                conflictBlocks,
                blockedBlocks),
            adminView);
    }

    public async Task<FieldInventoryPracticeCoachResponse> CreatePracticeRequestAsync(FieldInventoryPracticeRequestCreateRequest request, string userId, CorrelationContext context)
    {
        var actor = await RequireCoachMembershipAsync(context.LeagueId, userId);
        var bundle = await LoadBundleAsync(context.LeagueId, request.SeasonLabel);
        var practiceSlotKey = (request.PracticeSlotKey ?? "").Trim();
        var slot = bundle.Blocks.FirstOrDefault(block => string.Equals(block.PracticeSlotKey, practiceSlotKey, StringComparison.OrdinalIgnoreCase));
        if (slot is null)
        {
            throw new ApiGuards.HttpError(404, ErrorCodes.PRACTICE_SPACE_NOT_FOUND, "Practice space not found.");
        }

        var teamId = actor.IsLeagueAdminOrGlobalAdmin
            ? (request.TeamId ?? "").Trim() is var requestedTeamId && !string.IsNullOrWhiteSpace(requestedTeamId) ? requestedTeamId : actor.TeamId
            : actor.TeamId;
        if (string.IsNullOrWhiteSpace(teamId))
        {
            throw new ApiGuards.HttpError(403, ErrorCodes.COACH_TEAM_REQUIRED, "Your coach profile needs a team assignment before requesting practice space.");
        }

        if (string.IsNullOrWhiteSpace(slot.CanonicalDivisionCode))
        {
            throw new ApiGuards.HttpError(409, ErrorCodes.PRACTICE_NORMALIZATION_CONFLICT, "This practice space is missing a canonical division mapping.");
        }

        if (!actor.IsLeagueAdminOrGlobalAdmin &&
            !string.Equals(actor.TeamDivision, slot.CanonicalDivisionCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new ApiGuards.HttpError(409, ErrorCodes.DIVISION_MISMATCH, "This practice space belongs to a different division.");
        }

        if (slot.BookingPolicy == FieldInventoryPracticeBookingPolicies.NotRequestable)
        {
            throw new ApiGuards.HttpError(409, ErrorCodes.PRACTICE_SPACE_NOT_REQUESTABLE, "This field space is not requestable through SportsCH.");
        }

        if (!string.Equals(slot.NormalizationState, "ready", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(slot.NormalizationState, "normalized", StringComparison.OrdinalIgnoreCase))
        {
            throw new ApiGuards.HttpError(409, ErrorCodes.PRACTICE_NORMALIZATION_CONFLICT, "This practice space must be normalized or resolved before it can be requested.");
        }

        var canonicalSlot = await EnsureCanonicalSlotAsync(slot, userId);
        var team = await _teamRepository.GetTeamAsync(context.LeagueId, slot.CanonicalDivisionCode, teamId);
        if (team is null)
        {
            throw new ApiGuards.HttpError(404, ErrorCodes.TEAM_NOT_FOUND, "Team not found.");
        }

        var createdRequest = await _practiceRequestService.CreateRequestAsync(
            context.LeagueId,
            userId,
            slot.CanonicalDivisionCode,
            teamId,
            canonicalSlot.RowKey,
            request.Notes,
            request.OpenToShareField,
            request.ShareWithTeamId);

        if (string.Equals(slot.BookingPolicy, FieldInventoryPracticeBookingPolicies.AutoApprove, StringComparison.OrdinalIgnoreCase))
        {
            await _practiceRequestService.AutoApproveRequestAsync(
                context.LeagueId,
                createdRequest.RowKey,
                userId,
                "Auto-approved from normalized field inventory.");
        }

        return await GetCoachViewAsync(bundle.SeasonLabel, userId, context);
    }

    public async Task<FieldInventoryPracticeCoachResponse> MovePracticeRequestAsync(string requestId, FieldInventoryPracticeRequestMoveRequest request, string userId, CorrelationContext context)
    {
        var actor = await RequireCoachMembershipAsync(context.LeagueId, userId);
        var sourceRequest = await _practiceRequestRepository.GetRequestAsync(context.LeagueId, (requestId ?? "").Trim());
        if (sourceRequest is null)
        {
            throw new ApiGuards.HttpError(404, ErrorCodes.PRACTICE_REQUEST_NOT_FOUND, "Practice request not found.");
        }

        var sourceDivision = (sourceRequest.GetString("Division") ?? "").Trim();
        var sourceTeamId = (sourceRequest.GetString("TeamId") ?? "").Trim();
        var sourceStatus = (sourceRequest.GetString("Status") ?? "").Trim();
        if (!actor.IsLeagueAdminOrGlobalAdmin &&
            (!string.Equals(actor.TeamDivision, sourceDivision, StringComparison.OrdinalIgnoreCase) ||
             !string.Equals(actor.TeamId, sourceTeamId, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ApiGuards.HttpError(403, ErrorCodes.FORBIDDEN, "You can only move your own team's practice requests.");
        }

        if (!string.Equals(sourceStatus, FieldInventoryPracticeRequestStatuses.Approved, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(sourceStatus, FieldInventoryPracticeRequestStatuses.Pending, StringComparison.OrdinalIgnoreCase))
        {
            throw new ApiGuards.HttpError(409, ErrorCodes.PRACTICE_MOVE_NOT_ALLOWED, $"Only active practice requests can be moved (status: {sourceStatus}).");
        }

        var bundle = await LoadBundleAsync(context.LeagueId, request.SeasonLabel ?? sourceRequest.GetString("PracticeSeasonLabel"));
        var practiceSlotKey = (request.PracticeSlotKey ?? "").Trim();
        var slot = bundle.Blocks.FirstOrDefault(block => string.Equals(block.PracticeSlotKey, practiceSlotKey, StringComparison.OrdinalIgnoreCase));
        if (slot is null)
        {
            throw new ApiGuards.HttpError(404, ErrorCodes.PRACTICE_SPACE_NOT_FOUND, "Practice space not found.");
        }

        if (string.IsNullOrWhiteSpace(slot.CanonicalDivisionCode) ||
            !string.Equals(slot.CanonicalDivisionCode, sourceDivision, StringComparison.OrdinalIgnoreCase))
        {
            throw new ApiGuards.HttpError(409, ErrorCodes.DIVISION_MISMATCH, "Moves must stay within the original request division.");
        }

        if (slot.BookingPolicy == FieldInventoryPracticeBookingPolicies.NotRequestable)
        {
            throw new ApiGuards.HttpError(409, ErrorCodes.PRACTICE_SPACE_NOT_REQUESTABLE, "This field space is not requestable through SportsCH.");
        }

        if (!string.Equals(slot.NormalizationState, "ready", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(slot.NormalizationState, "normalized", StringComparison.OrdinalIgnoreCase))
        {
            throw new ApiGuards.HttpError(409, ErrorCodes.PRACTICE_NORMALIZATION_CONFLICT, "This practice space must be normalized or resolved before it can be requested.");
        }

        var canonicalSlot = await EnsureCanonicalSlotAsync(slot, userId);
        var createdMove = await _practiceRequestService.CreateMoveRequestAsync(
            context.LeagueId,
            userId,
            sourceRequest.RowKey,
            canonicalSlot.RowKey,
            request.Notes,
            request.OpenToShareField,
            request.ShareWithTeamId);

        if (string.Equals(slot.BookingPolicy, FieldInventoryPracticeBookingPolicies.AutoApprove, StringComparison.OrdinalIgnoreCase))
        {
            await _practiceRequestService.AutoApproveRequestAsync(
                context.LeagueId,
                createdMove.RowKey,
                userId,
                "Moved and auto-approved from normalized field inventory.");
        }

        return await GetCoachViewAsync(bundle.SeasonLabel, userId, context);
    }

    public async Task<FieldInventoryPracticeAdminResponse> ApprovePracticeRequestAsync(string requestId, FieldInventoryPracticeRequestDecisionRequest request, string userId, CorrelationContext context)
    {
        var entity = await _practiceRequestRepository.GetRequestAsync(context.LeagueId, (requestId ?? "").Trim());
        if (entity is null)
        {
            throw new ApiGuards.HttpError(404, ErrorCodes.PRACTICE_REQUEST_NOT_FOUND, "Practice request not found.");
        }

        await _practiceRequestService.ApproveRequestAsync(context.LeagueId, userId, entity.RowKey, request.Reason);
        return await GetAdminViewAsync(entity.GetString("PracticeSeasonLabel"), context);
    }

    public async Task<FieldInventoryPracticeAdminResponse> RejectPracticeRequestAsync(string requestId, FieldInventoryPracticeRequestDecisionRequest request, string userId, CorrelationContext context)
    {
        var entity = await _practiceRequestRepository.GetRequestAsync(context.LeagueId, (requestId ?? "").Trim());
        if (entity is null)
        {
            throw new ApiGuards.HttpError(404, ErrorCodes.PRACTICE_REQUEST_NOT_FOUND, "Practice request not found.");
        }

        await _practiceRequestService.RejectRequestAsync(context.LeagueId, userId, entity.RowKey, request.Reason);
        return await GetAdminViewAsync(entity.GetString("PracticeSeasonLabel"), context);
    }

    public async Task<FieldInventoryPracticeCoachResponse> CancelPracticeRequestAsync(string requestId, string userId, CorrelationContext context)
    {
        var entity = await _practiceRequestRepository.GetRequestAsync(context.LeagueId, (requestId ?? "").Trim());
        if (entity is null)
        {
            throw new ApiGuards.HttpError(404, ErrorCodes.PRACTICE_REQUEST_NOT_FOUND, "Practice request not found.");
        }

        await _practiceRequestService.CancelRequestAsync(context.LeagueId, userId, entity.RowKey, "Cancelled");
        return await GetCoachViewAsync(entity.GetString("PracticeSeasonLabel"), userId, context);
    }

    public async Task<PracticeConflictCheckResponse> CheckMoveConflictsAsync(string seasonLabel, string practiceSlotKey, string userId, CorrelationContext context)
    {
        var membership = await RequireCoachMembershipAsync(context.LeagueId, userId);
        var bundle = await LoadBundleAsync(context.LeagueId, seasonLabel);

        // Find the practice slot the coach wants to move to
        var targetSlot = bundle.Blocks.FirstOrDefault(block =>
            string.Equals(block.PracticeSlotKey, practiceSlotKey, StringComparison.OrdinalIgnoreCase));

        if (targetSlot is null)
        {
            throw new ApiGuards.HttpError(404, ErrorCodes.PRACTICE_SPACE_NOT_FOUND, "Practice space not found.");
        }

        var conflicts = new List<PracticeConflictDto>();
        var targetDate = (targetSlot.Date ?? "").Trim();
        var targetStart = (targetSlot.StartTime ?? "").Trim();
        var targetEnd = (targetSlot.EndTime ?? "").Trim();

        if (!TimeUtil.TryParseMinutes(targetStart, out var targetStartMin) ||
            !TimeUtil.TryParseMinutes(targetEnd, out var targetEndMin))
        {
            return new PracticeConflictCheckResponse(false, conflicts);
        }

        // Query all slots for the team on the same date
        var slotsOnDate = await _slotRepository.QuerySlotsAsync(new SlotQueryFilter
        {
            LeagueId = context.LeagueId,
            Division = membership.TeamDivision,
            FromDate = targetDate,
            ToDate = targetDate,
            ExcludeCancelled = true,
            ExcludeAvailability = true,
            PageSize = 100
        });

        // Check each slot for time overlap
        foreach (var slot in slotsOnDate.Items)
        {
            var slotStatus = (slot.GetString("Status") ?? "").Trim();
            if (string.Equals(slotStatus, Constants.Status.SlotCancelled, StringComparison.OrdinalIgnoreCase))
                continue;

            // Check if this slot involves the coach's team
            var offeringTeamId = (slot.GetString("OfferingTeamId") ?? "").Trim();
            var confirmedTeamId = (slot.GetString("ConfirmedTeamId") ?? "").Trim();
            var homeTeamId = (slot.GetString("HomeTeamId") ?? "").Trim();
            var awayTeamId = (slot.GetString("AwayTeamId") ?? "").Trim();

            var teamInvolvement =
                string.Equals(offeringTeamId, membership.TeamId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(confirmedTeamId, membership.TeamId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(homeTeamId, membership.TeamId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(awayTeamId, membership.TeamId, StringComparison.OrdinalIgnoreCase);

            if (!teamInvolvement)
                continue;

            // Parse slot times
            var slotStart = (slot.GetString("StartTime") ?? "").Trim();
            var slotEnd = (slot.GetString("EndTime") ?? "").Trim();

            if (!TimeUtil.TryParseMinutes(slotStart, out var slotStartMin) ||
                !TimeUtil.TryParseMinutes(slotEnd, out var slotEndMin))
                continue;

            // Check for time overlap
            if (!TimeUtil.Overlaps(targetStartMin, targetEndMin, slotStartMin, slotEndMin))
                continue;

            // Found a conflict - determine type and opponent
            var isAvailability = slot.GetBoolean("IsAvailability") ?? false;
            var isGame = !isAvailability &&
                        !string.Equals(slot.GetString("GameType"), "practice", StringComparison.OrdinalIgnoreCase);

            var opponent = "";
            if (isGame)
            {
                if (string.Equals(homeTeamId, membership.TeamId, StringComparison.OrdinalIgnoreCase))
                    opponent = awayTeamId;
                else if (string.Equals(awayTeamId, membership.TeamId, StringComparison.OrdinalIgnoreCase))
                    opponent = homeTeamId;
            }

            var location = (slot.GetString("DisplayName") ?? slot.GetString("FieldName") ?? "Unknown").Trim();

            conflicts.Add(new PracticeConflictDto(
                Type: isGame ? "game" : "practice",
                Date: targetDate,
                StartTime: slotStart,
                EndTime: slotEnd,
                Location: location,
                Opponent: string.IsNullOrWhiteSpace(opponent) ? null : opponent,
                Status: slotStatus));
        }

        // Also check for existing approved practice requests on the same date/time
        var practiceRequests = await _practiceRequestRepository.QueryRequestsAsync(
            context.LeagueId, null, membership.TeamDivision, membership.TeamId, null);

        foreach (var request in practiceRequests ?? Enumerable.Empty<TableEntity>())
        {
            var requestStatus = (request.GetString("Status") ?? "").Trim();
            if (!string.Equals(requestStatus, FieldInventoryPracticeRequestStatuses.Approved, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(requestStatus, FieldInventoryPracticeRequestStatuses.Pending, StringComparison.OrdinalIgnoreCase))
                continue;

            var requestDate = (request.GetString("Date") ?? "").Trim();
            if (!string.Equals(requestDate, targetDate, StringComparison.OrdinalIgnoreCase))
                continue;

            var requestStart = (request.GetString("StartTime") ?? "").Trim();
            var requestEnd = (request.GetString("EndTime") ?? "").Trim();

            if (!TimeUtil.TryParseMinutes(requestStart, out var requestStartMin) ||
                !TimeUtil.TryParseMinutes(requestEnd, out var requestEndMin))
                continue;

            if (!TimeUtil.Overlaps(targetStartMin, targetEndMin, requestStartMin, requestEndMin))
                continue;

            var requestLocation = (request.GetString("DisplayName") ?? request.GetString("FieldName") ?? "Unknown").Trim();

            conflicts.Add(new PracticeConflictDto(
                Type: "practice",
                Date: requestDate,
                StartTime: requestStart,
                EndTime: requestEnd,
                Location: requestLocation,
                Opponent: null,
                Status: requestStatus));
        }

        return new PracticeConflictCheckResponse(conflicts.Count > 0, conflicts);
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
            ? []
            : await _inventoryRepository.GetLiveRecordsAsync(leagueId, resolvedSeason);

        var divisions = await _divisionRepository.QueryDivisionsAsync(leagueId);
        var teams = await _teamRepository.QueryAllTeamsAsync(leagueId);
        var divisionAliases = await _inventoryRepository.GetDivisionAliasesAsync(leagueId);
        var teamAliases = await _inventoryRepository.GetTeamAliasesAsync(leagueId);
        var groupPolicies = await _inventoryRepository.GetGroupPoliciesAsync(leagueId);

        var alignedRecords = BuildAlignedRecords(
            resolvedSeason,
            liveRecords,
            divisions,
            teams,
            divisionAliases,
            teamAliases,
            groupPolicies);
        var blocks = BuildPracticeBlocks(alignedRecords);
        var relevantSlots = await LoadRelevantSlotsAsync(leagueId, blocks);
        ApplySlotState(blocks, relevantSlots);

        var relevantRequests = await LoadRelevantRequestsAsync(leagueId, resolvedSeason, blocks, relevantSlots);
        var teamNameLookup = teams.ToDictionary(
            team => $"{(team.GetString("Division") ?? "").Trim()}|{team.RowKey}",
            team => team.GetString("Name") ?? team.RowKey,
            StringComparer.OrdinalIgnoreCase);
        var requestDtos = BuildRequestDtos(relevantRequests, relevantSlots, blocks, teamNameLookup);
        ApplyRequestState(blocks, requestDtos);
        var adminRows = BuildAdminRows(alignedRecords, blocks);

        return new Bundle
        {
            LeagueId = leagueId,
            SeasonLabel = resolvedSeason,
            SeasonOptions = seasons.Select((label, index) => new FieldInventoryPracticeSeasonOptionDto(
                label,
                index == 0 && string.IsNullOrWhiteSpace(seasonLabel) || string.Equals(label, resolvedSeason, StringComparison.OrdinalIgnoreCase))).ToList(),
            AdminRows = adminRows,
            Blocks = blocks.OrderBy(x => x.Date).ThenBy(x => x.StartTime).ThenBy(x => x.FieldName).ToList(),
            Requests = requestDtos
                .OrderByDescending(x => x.Date)
                .ThenByDescending(x => x.StartTime)
                .ThenBy(x => x.FieldName, StringComparer.OrdinalIgnoreCase)
                .ToList(),
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

    private async Task<List<TableEntity>> LoadRelevantSlotsAsync(string leagueId, IReadOnlyCollection<PracticeBlockCandidate> blocks)
    {
        if (blocks.Count == 0)
        {
            return [];
        }

        var dateFrom = blocks.Min(block => block.Date);
        var dateTo = blocks.Max(block => block.Date);
        var relevantFields = blocks
            .Select(block => NormalizeFieldKey(block.FieldId))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var slots = await QueryAllSlotsAsync(new SlotQueryFilter
        {
            LeagueId = leagueId,
            FromDate = dateFrom,
            ToDate = dateTo,
            PageSize = 500,
        });

        return slots
            .Where(slot =>
            {
                var fieldKey = NormalizeFieldKey(slot.GetString("FieldKey"));
                return relevantFields.Contains(fieldKey) &&
                       !string.Equals(slot.GetString("Status"), Constants.Status.SlotCancelled, StringComparison.OrdinalIgnoreCase);
            })
            .ToList();
    }

    private async Task<List<TableEntity>> LoadRelevantRequestsAsync(
        string leagueId,
        string seasonLabel,
        IReadOnlyCollection<PracticeBlockCandidate> blocks,
        IReadOnlyCollection<TableEntity> slots)
    {
        if (blocks.Count == 0)
        {
            return [];
        }

        var slotIds = blocks
            .Select(block => block.EffectiveSlotId)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var fieldKeys = blocks
            .Select(block => NormalizeFieldKey(block.FieldId))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var dateFrom = blocks.Min(block => block.Date);
        var dateTo = blocks.Max(block => block.Date);
        var slotLookup = slots.ToDictionary(slot => $"{(slot.GetString("Division") ?? "").Trim()}|{slot.RowKey}", StringComparer.OrdinalIgnoreCase);

        var requests = await _practiceRequestRepository.QueryRequestsAsync(leagueId, null, null, null, null);
        return requests.Where(request =>
        {
            var requestSeason = (request.GetString("PracticeSeasonLabel") ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(requestSeason) &&
                string.Equals(requestSeason, seasonLabel, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var slotId = (request.GetString("SlotId") ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(slotId) && slotIds.Contains(slotId))
            {
                return true;
            }

            var fieldKey = NormalizeFieldKey(request.GetString("FieldKey"));
            var gameDate = (request.GetString("GameDate") ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(fieldKey) &&
                !string.IsNullOrWhiteSpace(gameDate) &&
                fieldKeys.Contains(fieldKey) &&
                string.CompareOrdinal(gameDate, dateFrom) >= 0 &&
                string.CompareOrdinal(gameDate, dateTo) <= 0)
            {
                return true;
            }

            var division = (request.GetString("Division") ?? "").Trim();
            if (string.IsNullOrWhiteSpace(division) || string.IsNullOrWhiteSpace(slotId))
            {
                return false;
            }

            if (!slotLookup.TryGetValue($"{division}|{slotId}", out var slot))
            {
                return false;
            }

            var slotFieldKey = NormalizeFieldKey(slot.GetString("FieldKey"));
            var slotDate = (slot.GetString("GameDate") ?? "").Trim();
            return fieldKeys.Contains(slotFieldKey) &&
                   string.CompareOrdinal(slotDate, dateFrom) >= 0 &&
                   string.CompareOrdinal(slotDate, dateTo) <= 0;
        }).ToList();
    }

    private async Task<List<TableEntity>> QueryAllSlotsAsync(SlotQueryFilter filter)
    {
        var all = new List<TableEntity>();
        string? continuation = null;
        do
        {
            var page = await _slotRepository.QuerySlotsAsync(filter, continuation);
            if (page.Items.Count > 0)
            {
                all.AddRange(page.Items);
            }
            continuation = page.ContinuationToken;
        }
        while (!string.IsNullOrWhiteSpace(continuation));

        return all;
    }

    private async Task<TableEntity> EnsureCanonicalSlotAsync(PracticeBlockCandidate block, string userId)
    {
        var now = DateTimeOffset.UtcNow;
        if (block.ExactSlot is not null)
        {
            if (!SlotEntityUtil.IsAvailability(block.ExactSlot))
            {
                throw new ApiGuards.HttpError(409, ErrorCodes.PRACTICE_NORMALIZATION_CONFLICT, "An existing non-availability slot already occupies this imported practice block.");
            }

            if (block.NeedsSlotMetadataSync)
            {
                ApplyCanonicalSlotMetadata(block.ExactSlot, block, userId, now);
                await _slotRepository.UpdateSlotAsync(block.ExactSlot, block.ExactSlot.ETag);
                block.NeedsSlotMetadataSync = false;
            }

            return block.ExactSlot;
        }

        if (!string.Equals(block.NormalizationState, "ready", StringComparison.OrdinalIgnoreCase))
        {
            throw new ApiGuards.HttpError(409, ErrorCodes.PRACTICE_NORMALIZATION_CONFLICT, "This imported practice block cannot be normalized until its conflicts are resolved.");
        }

        var entity = BuildCanonicalSlotEntity(block, userId, now);
        try
        {
            await _slotRepository.CreateSlotAsync(entity);
            block.ExactSlot = entity;
            block.EffectiveSlotId = entity.RowKey;
            block.NormalizationState = "normalized";
            block.NeedsSlotMetadataSync = false;
            return entity;
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            var existing = await _slotRepository.GetSlotAsync(block.LeagueId, block.CanonicalDivisionCode, block.DeterministicSlotId);
            if (existing is null)
            {
                throw;
            }

            block.ExactSlot = existing;
            block.EffectiveSlotId = existing.RowKey;
            block.NormalizationState = "normalized";
            block.NeedsSlotMetadataSync = NeedsCanonicalSlotUpdate(existing, block);
            if (block.NeedsSlotMetadataSync)
            {
                ApplyCanonicalSlotMetadata(existing, block, userId, now);
                await _slotRepository.UpdateSlotAsync(existing, existing.ETag);
                block.NeedsSlotMetadataSync = false;
            }

            return existing;
        }
    }

    private static List<PracticeBlockCandidate> FilterBlocks(IEnumerable<PracticeBlockCandidate> blocks, string? dateFrom, string? dateTo, string? fieldId)
    {
        var from = (dateFrom ?? "").Trim();
        var to = (dateTo ?? "").Trim();
        var field = NormalizeFieldKey(fieldId);
        return blocks
            .Where(block => string.IsNullOrWhiteSpace(from) || string.CompareOrdinal(block.Date, from) >= 0)
            .Where(block => string.IsNullOrWhiteSpace(to) || string.CompareOrdinal(block.Date, to) <= 0)
            .Where(block => string.IsNullOrWhiteSpace(field) || string.Equals(NormalizeFieldKey(block.FieldId), field, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static List<AlignedRecord> BuildAlignedRecords(
        string seasonLabel,
        IReadOnlyCollection<FieldInventoryLiveRecordEntity> liveRecords,
        IReadOnlyCollection<TableEntity> divisions,
        IReadOnlyCollection<TableEntity> teams,
        IReadOnlyCollection<FieldInventoryDivisionAliasEntity> divisionAliases,
        IReadOnlyCollection<FieldInventoryTeamAliasEntity> teamAliases,
        IReadOnlyCollection<FieldInventoryGroupPolicyEntity> groupPolicies)
    {
        var divisionOptions = divisions
            .Select(d => new DivisionOption(d.GetString("Code") ?? d.RowKey, d.GetString("Name") ?? d.GetString("Code") ?? d.RowKey))
            .ToList();
        var teamOptions = teams
            .Select(t => new TeamOption(t.GetString("Division") ?? "", t.RowKey, t.GetString("Name") ?? t.RowKey))
            .ToList();

        return liveRecords
            .OrderBy(record => record.Date)
            .ThenBy(record => record.StartTime)
            .ThenBy(record => record.FieldName)
            .Select(record =>
            {
                var divisionMatch = ResolveDivision(record.AssignedDivision, divisionOptions, divisionAliases);
                var teamMatch = ResolveTeam(record.AssignedTeamOrEvent, divisionMatch.Code, teamOptions, teamAliases);
                var policy = ResolvePolicy(record, groupPolicies);
                var issues = new List<string>();
                if (string.IsNullOrWhiteSpace(record.FieldId)) issues.Add("field_unmapped");
                if (!string.IsNullOrWhiteSpace(record.AssignedDivision) && string.IsNullOrWhiteSpace(divisionMatch.Code)) issues.Add("division_unmapped");
                if (!string.IsNullOrWhiteSpace(record.AssignedTeamOrEvent) && string.IsNullOrWhiteSpace(teamMatch.TeamId)) issues.Add("team_unmapped");
                if (policy.BookingPolicy == FieldInventoryPracticeBookingPolicies.NotRequestable && policy.IsActionable) issues.Add("policy_unmapped");
                return new AlignedRecord(seasonLabel, record, divisionMatch, teamMatch, policy, issues);
            })
            .ToList();
    }

    private static List<PracticeBlockCandidate> BuildPracticeBlocks(IEnumerable<AlignedRecord> alignedRecords)
    {
        var blocks = new List<PracticeBlockCandidate>();
        foreach (var aligned in alignedRecords)
        {
            if (!TryParseMinutes(aligned.Record.StartTime, out var startMinutes) ||
                !TryParseMinutes(aligned.Record.EndTime, out var endMinutes) ||
                endMinutes - startMinutes < 90)
            {
                continue;
            }

            for (var blockStart = startMinutes; blockStart + 90 <= endMinutes; blockStart += 90)
            {
                var blockEnd = blockStart + 90;
                var issues = new List<string>(aligned.MappingIssues);
                var availabilityAvailable = string.Equals(aligned.Record.AvailabilityStatus, FieldInventoryAvailabilityStatuses.Available, StringComparison.OrdinalIgnoreCase);
                var notUsed = string.Equals(aligned.Record.UtilizationStatus, FieldInventoryUtilizationStatuses.NotUsed, StringComparison.OrdinalIgnoreCase);
                var baseRequestable = availabilityAvailable &&
                    notUsed &&
                    !string.IsNullOrWhiteSpace(aligned.Record.FieldId) &&
                    !string.IsNullOrWhiteSpace(aligned.Division.Code) &&
                    !string.Equals(aligned.Policy.BookingPolicy, FieldInventoryPracticeBookingPolicies.NotRequestable, StringComparison.OrdinalIgnoreCase);

                if (!availabilityAvailable || !notUsed)
                {
                    issues.Add("imported_not_requestable");
                }

                blocks.Add(new PracticeBlockCandidate
                {
                    LeagueId = aligned.Record.LeagueId,
                    PracticeSlotKey = $"{aligned.Record.Id}|{FormatMinutes(blockStart)}|{FormatMinutes(blockEnd)}",
                    SeasonLabel = aligned.SeasonLabel,
                    LiveRecordId = aligned.Record.Id,
                    Date = aligned.Record.Date,
                    DayOfWeek = aligned.Record.DayOfWeek,
                    StartTime = FormatMinutes(blockStart),
                    EndTime = FormatMinutes(blockEnd),
                    SlotDurationMinutes = 90,
                    FieldId = NormalizeFieldKey(aligned.Record.FieldId),
                    FieldName = aligned.Record.FieldName,
                    BookingPolicy = aligned.Policy.BookingPolicy,
                    BookingPolicyReason = aligned.Policy.Reason,
                    AssignedGroup = aligned.Record.AssignedGroup,
                    AssignedDivision = aligned.Record.AssignedDivision,
                    AssignedTeamOrEvent = aligned.Record.AssignedTeamOrEvent,
                    CanonicalDivisionCode = aligned.Division.Code ?? "",
                    CanonicalDivisionName = aligned.Division.Name,
                    CanonicalTeamId = aligned.Team.TeamId,
                    CanonicalTeamName = aligned.Team.TeamName,
                    BaseRequestable = baseRequestable,
                    MappingIssues = issues,
                    DeterministicSlotId = SlotKeyUtil.BuildAvailabilitySlotId(aligned.Record.Date, FormatMinutes(blockStart), FormatMinutes(blockEnd), aligned.Record.FieldId),
                    EffectiveSlotId = SlotKeyUtil.BuildAvailabilitySlotId(aligned.Record.Date, FormatMinutes(blockStart), FormatMinutes(blockEnd), aligned.Record.FieldId),
                    NormalizationState = baseRequestable ? "ready" : "blocked",
                    NormalizationIssues = new List<string>(issues),
                });
            }
        }

        return blocks;
    }

    private static void ApplySlotState(IReadOnlyCollection<PracticeBlockCandidate> blocks, IReadOnlyCollection<TableEntity> slots)
    {
        var exactLookup = slots
            .GroupBy(slot => BuildIdentity(
                (slot.GetString("Division") ?? "").Trim(),
                slot.GetString("FieldKey") ?? "",
                slot.GetString("GameDate") ?? "",
                slot.GetString("StartTime") ?? "",
                slot.GetString("EndTime") ?? ""), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var slotsByFieldDate = slots
            .GroupBy(slot => $"{NormalizeFieldKey(slot.GetString("FieldKey"))}|{(slot.GetString("GameDate") ?? "").Trim()}", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        foreach (var block in blocks)
        {
            var identity = BuildIdentity(block.CanonicalDivisionCode, block.FieldId, block.Date, block.StartTime, block.EndTime);
            if (!string.IsNullOrWhiteSpace(block.CanonicalDivisionCode) && exactLookup.TryGetValue(identity, out var exact))
            {
                block.ExactSlot = exact;
                block.EffectiveSlotId = exact.RowKey;
                block.NeedsSlotMetadataSync = NeedsCanonicalSlotUpdate(exact, block);
                if (SlotEntityUtil.IsAvailability(exact))
                {
                    block.NormalizationState = "normalized";
                    if (!string.Equals(block.DeterministicSlotId, exact.RowKey, StringComparison.OrdinalIgnoreCase))
                    {
                        block.NormalizationIssues.Add("legacy_slot_id");
                    }
                }
                else
                {
                    block.NormalizationState = "conflict";
                    block.NormalizationIssues.Add("slot_already_in_use");
                }
            }

            var overlapKey = $"{NormalizeFieldKey(block.FieldId)}|{block.Date}";
            if (slotsByFieldDate.TryGetValue(overlapKey, out var sameFieldDate))
            {
                var overlaps = sameFieldDate
                    .Where(slot =>
                    {
                        if (block.ExactSlot is not null && string.Equals(slot.RowKey, block.ExactSlot.RowKey, StringComparison.OrdinalIgnoreCase))
                        {
                            return false;
                        }

                        return TimeUtil.IsValidRange(block.StartTime, block.EndTime, out var blockStart, out var blockEnd, out _) &&
                               TimeUtil.IsValidRange(slot.GetString("StartTime") ?? "", slot.GetString("EndTime") ?? "", out var slotStart, out var slotEnd, out _) &&
                               TimeUtil.Overlaps(blockStart, blockEnd, slotStart, slotEnd);
                    })
                    .ToList();

                if (overlaps.Count > 0)
                {
                    block.OverlapSlots = overlaps;
                    block.NormalizationState = "conflict";

                    if (overlaps.Any(slot => !string.Equals((slot.GetString("Division") ?? "").Trim(), block.CanonicalDivisionCode, StringComparison.OrdinalIgnoreCase)))
                    {
                        block.NormalizationIssues.Add("cross_division_overlap");
                    }
                    else
                    {
                        block.NormalizationIssues.Add("manual_overlap");
                    }
                }
            }

            if (string.Equals(block.NormalizationState, "ready", StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrWhiteSpace(block.CanonicalDivisionCode) || string.IsNullOrWhiteSpace(block.FieldId)))
            {
                block.NormalizationState = "blocked";
            }
        }
    }

    private static void ApplyRequestState(IReadOnlyCollection<PracticeBlockCandidate> blocks, IReadOnlyCollection<FieldInventoryPracticeRequestDto> requests)
    {
        var requestsBySlot = requests
            .GroupBy(request => $"{request.Division}|{request.SlotId}", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        foreach (var block in blocks)
        {
            var key = $"{block.CanonicalDivisionCode}|{block.EffectiveSlotId}";
            if (string.IsNullOrWhiteSpace(block.CanonicalDivisionCode) || string.IsNullOrWhiteSpace(block.EffectiveSlotId) || !requestsBySlot.TryGetValue(key, out var slotRequests))
            {
                block.IsAvailable =
                    string.Equals(block.NormalizationState, "ready", StringComparison.OrdinalIgnoreCase) ||
                    (string.Equals(block.NormalizationState, "normalized", StringComparison.OrdinalIgnoreCase) &&
                     block.ExactSlot is not null &&
                     string.Equals((block.ExactSlot.GetString("Status") ?? Constants.Status.SlotOpen).Trim(), Constants.Status.SlotOpen, StringComparison.OrdinalIgnoreCase))
                        ? true
                        : false;
                block.PendingTeamIds = [];
                block.ReservedTeamIds = [];
                block.PendingShareTeamIds = [];
                continue;
            }

            var pendingTeamIds = slotRequests
                .Where(request => string.Equals(request.Status, FieldInventoryPracticeRequestStatuses.Pending, StringComparison.OrdinalIgnoreCase))
                .Select(request => request.TeamId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var pendingShareTeamIds = slotRequests
                .Where(request => string.Equals(request.Status, FieldInventoryPracticeRequestStatuses.Pending, StringComparison.OrdinalIgnoreCase))
                .Select(request => request.ShareWithTeamId ?? "")
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var reservedTeamIds = slotRequests
                .SelectMany(request => request.ReservedTeamIds ?? [])
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            block.IsAvailable = false;
            block.PendingTeamIds = pendingTeamIds;
            block.PendingShareTeamIds = pendingShareTeamIds;
            block.ReservedTeamIds = reservedTeamIds;
        }
    }

    private static List<FieldInventoryPracticeRequestDto> BuildRequestDtos(
        IReadOnlyCollection<TableEntity> requests,
        IReadOnlyCollection<TableEntity> slots,
        IReadOnlyCollection<PracticeBlockCandidate> blocks,
        IReadOnlyDictionary<string, string> teamNameLookup)
    {
        var slotLookup = slots.ToDictionary(slot => $"{(slot.GetString("Division") ?? "").Trim()}|{slot.RowKey}", StringComparer.OrdinalIgnoreCase);
        var requestLookup = requests.ToDictionary(request => request.RowKey, StringComparer.OrdinalIgnoreCase);
        var blockLookup = blocks
            .GroupBy(block => $"{block.CanonicalDivisionCode}|{block.EffectiveSlotId}", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        return requests.Select(request =>
        {
            var division = (request.GetString("Division") ?? "").Trim();
            var slotId = (request.GetString("SlotId") ?? "").Trim();
            slotLookup.TryGetValue($"{division}|{slotId}", out var slot);
            blockLookup.TryGetValue($"{division}|{slotId}", out var block);

            var date = (request.GetString("GameDate") ?? slot?.GetString("GameDate") ?? "").Trim();
            var startTime = (request.GetString("StartTime") ?? slot?.GetString("StartTime") ?? "").Trim();
            var endTime = (request.GetString("EndTime") ?? slot?.GetString("EndTime") ?? "").Trim();
            var fieldId = NormalizeFieldKey(request.GetString("FieldKey") ?? slot?.GetString("FieldKey") ?? block?.FieldId ?? "");
            var fieldName = FirstNonBlank(
                request.GetString("DisplayName"),
                request.GetString("FieldName"),
                slot?.GetString("DisplayName"),
                slot?.GetString("FieldName"),
                block?.FieldName,
                fieldId);
            var bookingPolicy = FirstNonBlank(
                request.GetString("PracticeBookingPolicy"),
                slot?.GetString("PracticeBookingPolicy"),
                block?.BookingPolicy,
                FieldInventoryPracticeBookingPolicies.CommissionerReview);
            var teamId = (request.GetString("TeamId") ?? "").Trim();
            var requestKind = (request.GetString("RequestKind") ?? "").Trim();
            var moveFromRequestId = (request.GetString("MoveFromRequestId") ?? "").Trim();
            var openToShareField = request.GetBoolean("OpenToShareField") ?? false;
            var shareWithTeamId = (request.GetString("ShareWithTeamId") ?? "").Trim();
            requestLookup.TryGetValue(moveFromRequestId, out var sourceRequest);
            var sourceDate = sourceRequest?.GetString("GameDate") ?? "";
            var sourceStart = sourceRequest?.GetString("StartTime") ?? "";
            var sourceEnd = sourceRequest?.GetString("EndTime") ?? "";
            var sourceFieldName = FirstNonBlank(
                sourceRequest?.GetString("DisplayName"),
                sourceRequest?.GetString("FieldName"));
            var reservedTeamIds = new List<string>();
            if (!string.IsNullOrWhiteSpace(teamId))
            {
                reservedTeamIds.Add(teamId);
            }

            if (openToShareField && !string.IsNullOrWhiteSpace(shareWithTeamId))
            {
                reservedTeamIds.Add(shareWithTeamId);
            }

            return new FieldInventoryPracticeRequestDto(
                request.RowKey,
                FirstNonBlank(request.GetString("PracticeSeasonLabel"), block?.SeasonLabel, ""),
                FirstNonBlank(request.GetString("PracticeSlotKey"), block?.PracticeSlotKey, slotId),
                FirstNonBlank(request.GetString("PracticeSourceRecordId"), block?.LiveRecordId, ""),
                slotId,
                division,
                date,
                WeekdayFromIso(date),
                startTime,
                endTime,
                fieldId,
                fieldName,
                teamId,
                teamNameLookup.TryGetValue($"{division}|{teamId}", out var teamName) ? teamName : null,
                (request.GetString("Status") ?? FieldInventoryPracticeRequestStatuses.Pending).Trim(),
                bookingPolicy,
                BookingPolicyLabel(bookingPolicy),
                string.Equals(requestKind, "Move", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrWhiteSpace(moveFromRequestId),
                string.IsNullOrWhiteSpace(moveFromRequestId) ? null : moveFromRequestId,
                string.IsNullOrWhiteSpace(sourceDate) ? null : sourceDate,
                string.IsNullOrWhiteSpace(sourceStart) ? null : sourceStart,
                string.IsNullOrWhiteSpace(sourceEnd) ? null : sourceEnd,
                string.IsNullOrWhiteSpace(sourceFieldName) ? null : sourceFieldName,
                (request.GetString("Reason") ?? "").Trim(),
                (request.GetString("RequestedBy") ?? "").Trim(),
                request.GetDateTimeOffset("RequestedUtc") ?? DateTimeOffset.MinValue,
                (request.GetString("ReviewedBy") ?? "").Trim(),
                request.GetDateTimeOffset("ReviewedUtc"),
                (request.GetString("ReviewReason") ?? "").Trim(),
                openToShareField,
                string.IsNullOrWhiteSpace(shareWithTeamId) ? null : shareWithTeamId,
                reservedTeamIds);
        }).ToList();
    }

    private static List<FieldInventoryPracticeAdminRowDto> BuildAdminRows(
        IReadOnlyCollection<AlignedRecord> alignedRecords,
        IReadOnlyCollection<PracticeBlockCandidate> blocks)
    {
        var blocksByRecordId = blocks
            .GroupBy(block => block.LiveRecordId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        return alignedRecords.Select(aligned =>
        {
            blocksByRecordId.TryGetValue(aligned.Record.Id, out var recordBlocks);
            recordBlocks ??= [];

            return new FieldInventoryPracticeAdminRowDto(
                aligned.Record.Id,
                aligned.SeasonLabel,
                aligned.Record.Date,
                aligned.Record.DayOfWeek,
                aligned.Record.StartTime,
                aligned.Record.EndTime,
                aligned.Record.SlotDurationMinutes,
                aligned.Record.AvailabilityStatus,
                aligned.Record.UtilizationStatus,
                aligned.Record.UsageType,
                aligned.Record.UsedBy,
                aligned.Record.FieldId,
                aligned.Record.FieldName,
                aligned.Record.RawFieldName,
                aligned.Record.AssignedGroup,
                aligned.Record.AssignedDivision,
                aligned.Record.AssignedTeamOrEvent,
                aligned.Division.Code,
                aligned.Division.Name,
                aligned.Team.TeamId,
                aligned.Team.TeamName,
                aligned.Policy.BookingPolicy,
                aligned.Policy.Reason,
                aligned.MappingIssues);
        }).ToList();
    }

    private static FieldInventoryPracticeAdminResponse BuildAdminResponse(Bundle bundle)
    {
        return new FieldInventoryPracticeAdminResponse(
            bundle.SeasonLabel,
            bundle.SeasonOptions,
            BuildSummary(bundle.Blocks, bundle.AdminRows, bundle.Requests),
            BuildNormalizationSummary(bundle.Blocks),
            bundle.AdminRows,
            bundle.Blocks.Select(MapSlot).ToList(),
            bundle.Requests,
            bundle.CanonicalFields,
            bundle.CanonicalDivisions,
            bundle.CanonicalTeams);
    }

    private static FieldInventoryPracticeCoachResponse BuildCoachResponse(Bundle bundle, string division, string teamId, string? teamName)
    {
        var requests = bundle.Requests
            .Where(request => string.Equals(request.TeamId, teamId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(request => request.Date)
            .ThenByDescending(request => request.StartTime)
            .ToList();
        var activeSlotIds = requests
            .Where(request => ActiveRequestStatuses.Contains(request.Status, StringComparer.OrdinalIgnoreCase))
            .Select(request => $"{request.Division}|{request.SlotId}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var slots = bundle.Blocks
            .Where(block => string.Equals(block.CanonicalDivisionCode, division, StringComparison.OrdinalIgnoreCase))
            .Where(block => block.BaseRequestable)
            .Where(block =>
                string.Equals(block.NormalizationState, "ready", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(block.NormalizationState, "normalized", StringComparison.OrdinalIgnoreCase))
            .Where(block => block.IsAvailable || activeSlotIds.Contains($"{block.CanonicalDivisionCode}|{block.EffectiveSlotId}"))
            .OrderBy(block => block.Date)
            .ThenBy(block => block.StartTime)
            .ThenBy(block => block.FieldName, StringComparer.OrdinalIgnoreCase)
            .Select(MapSlot)
            .ToList();

        return new FieldInventoryPracticeCoachResponse(
            bundle.SeasonLabel,
            bundle.SeasonOptions,
            division,
            teamId,
            teamName,
            BuildSummary(bundle.Blocks, bundle.AdminRows, bundle.Requests),
            slots,
            requests);
    }

    private static FieldInventoryPracticeSlotDto MapSlot(PracticeBlockCandidate block)
    {
        return new FieldInventoryPracticeSlotDto(
            block.PracticeSlotKey,
            block.SeasonLabel,
            block.LiveRecordId,
            block.EffectiveSlotId,
            block.CanonicalDivisionCode,
            block.Date,
            block.DayOfWeek,
            block.StartTime,
            block.EndTime,
            block.SlotDurationMinutes,
            block.FieldId,
            block.FieldName,
            block.BookingPolicy,
            BookingPolicyLabel(block.BookingPolicy),
            block.BookingPolicyReason,
            block.NormalizationState,
            block.NormalizationIssues,
            block.AssignedGroup,
            block.AssignedDivision,
            block.AssignedTeamOrEvent,
            block.IsAvailable,
            block.PendingTeamIds,
            block.Shareable,
            block.MaxTeamsPerBooking,
            block.ReservedTeamIds,
            block.PendingShareTeamIds);
    }

    private static FieldInventoryPracticeSummaryDto BuildSummary(
        IReadOnlyCollection<PracticeBlockCandidate> blocks,
        IReadOnlyCollection<FieldInventoryPracticeAdminRowDto> rows,
        IReadOnlyCollection<FieldInventoryPracticeRequestDto> requests)
    {
        var requestableBlocks = blocks.Count(block => block.BaseRequestable);
        var autoApproveBlocks = blocks.Count(block =>
            block.BaseRequestable &&
            string.Equals(block.BookingPolicy, FieldInventoryPracticeBookingPolicies.AutoApprove, StringComparison.OrdinalIgnoreCase));
        var commissionerReviewBlocks = blocks.Count(block =>
            block.BaseRequestable &&
            string.Equals(block.BookingPolicy, FieldInventoryPracticeBookingPolicies.CommissionerReview, StringComparison.OrdinalIgnoreCase));

        return new FieldInventoryPracticeSummaryDto(
            rows.Count,
            requestableBlocks,
            autoApproveBlocks,
            commissionerReviewBlocks,
            requests.Count(request => string.Equals(request.Status, FieldInventoryPracticeRequestStatuses.Pending, StringComparison.OrdinalIgnoreCase)),
            requests.Count(request => string.Equals(request.Status, FieldInventoryPracticeRequestStatuses.Approved, StringComparison.OrdinalIgnoreCase)),
            rows.Count(row => row.MappingIssues.Contains("division_unmapped", StringComparer.OrdinalIgnoreCase)),
            rows.Count(row => row.MappingIssues.Contains("team_unmapped", StringComparer.OrdinalIgnoreCase)),
            rows.Count(row => row.MappingIssues.Contains("policy_unmapped", StringComparer.OrdinalIgnoreCase)));
    }

    private static FieldInventoryPracticeNormalizationSummaryDto BuildNormalizationSummary(IReadOnlyCollection<PracticeBlockCandidate> blocks)
    {
        return new FieldInventoryPracticeNormalizationSummaryDto(
            blocks.Count,
            blocks.Count(block => string.Equals(block.NormalizationState, "normalized", StringComparison.OrdinalIgnoreCase)),
            blocks.Count(block => string.Equals(block.NormalizationState, "ready", StringComparison.OrdinalIgnoreCase)),
            blocks.Count(block => string.Equals(block.NormalizationState, "conflict", StringComparison.OrdinalIgnoreCase)),
            blocks.Count(block => string.Equals(block.NormalizationState, "blocked", StringComparison.OrdinalIgnoreCase)));
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
        var teamName = FirstNonBlank(
            membership?.GetString("TeamName"),
            membership?.GetString("CoachTeamName"));
        return new MembershipContext(role, isLeagueAdmin || isGlobalAdmin, division, teamId, teamName);
    }

    private static TableEntity BuildCanonicalSlotEntity(PracticeBlockCandidate block, string userId, DateTimeOffset now)
    {
        var entity = new TableEntity(Constants.Pk.Slots(block.LeagueId, block.CanonicalDivisionCode), block.DeterministicSlotId)
        {
            ["LeagueId"] = block.LeagueId,
            ["SlotId"] = block.DeterministicSlotId,
            ["Division"] = block.CanonicalDivisionCode,
            ["OfferingTeamId"] = "",
            ["HomeTeamId"] = "",
            ["AwayTeamId"] = "",
            ["IsExternalOffer"] = false,
            ["IsAvailability"] = true,
            ["OfferingEmail"] = "",
            ["GameDate"] = block.Date,
            ["StartTime"] = block.StartTime,
            ["EndTime"] = block.EndTime,
            ["Status"] = Constants.Status.SlotOpen,
            ["Notes"] = "Normalized from committed field inventory.",
            ["CreatedUtc"] = now,
            ["UpdatedUtc"] = now,
            ["LastUpdatedUtc"] = now,
        };

        ApplyCanonicalSlotMetadata(entity, block, userId, now);
        return entity;
    }

    private static void ApplyCanonicalSlotMetadata(TableEntity entity, PracticeBlockCandidate block, string userId, DateTimeOffset now)
    {
        var normalizedFieldKey = NormalizeFieldKey(block.FieldId);
        var displayName = FirstNonBlank(block.FieldName, normalizedFieldKey);
        var fieldName = displayName;
        var parkName = ExtractParkName(displayName, normalizedFieldKey);
        if (displayName.Contains('>', StringComparison.Ordinal))
        {
            var parts = displayName.Split('>', 2, StringSplitOptions.TrimEntries);
            parkName = FirstNonBlank(parts.ElementAtOrDefault(0), parkName);
            fieldName = FirstNonBlank(parts.ElementAtOrDefault(1), displayName);
        }

        entity["LeagueId"] = block.LeagueId;
        entity["SlotId"] = entity.RowKey;
        entity["Division"] = block.CanonicalDivisionCode;
        entity["GameDate"] = block.Date;
        SlotEntityUtil.ApplyTimeRange(entity, block.StartTime, block.EndTime);
        entity["FieldKey"] = normalizedFieldKey;
        entity["ParkName"] = parkName;
        entity["FieldName"] = fieldName;
        entity["DisplayName"] = displayName;
        entity["IsAvailability"] = true;
        entity["GameType"] = "Availability";
        entity["AllocationSlotType"] = "practice";
        entity["PracticeBookingPolicy"] = block.BookingPolicy;
        entity["PracticeSlotKey"] = block.PracticeSlotKey;
        entity["PracticeSourceRecordId"] = block.LiveRecordId;
        entity["PracticeSeasonLabel"] = block.SeasonLabel;
        entity["PracticeNormalizationState"] = block.NormalizationState;
        entity["PracticeAssignedGroup"] = block.AssignedGroup ?? "";
        entity["PracticeAssignedDivision"] = block.AssignedDivision ?? "";
        entity["PracticeAssignedTeamOrEvent"] = block.AssignedTeamOrEvent ?? "";
        entity["PracticeShareable"] = true;
        entity["PracticeMaxTeamsPerBooking"] = 2;
        entity["PracticeNormalizedBy"] = userId;
        entity["PracticeNormalizedUtc"] = now;
        entity["UpdatedUtc"] = now;
        entity["LastUpdatedUtc"] = now;
    }

    private static bool NeedsCanonicalSlotUpdate(TableEntity slot, PracticeBlockCandidate block)
    {
        var normalizedFieldKey = NormalizeFieldKey(block.FieldId);
        var displayName = FirstNonBlank(block.FieldName, normalizedFieldKey);
        var fieldName = displayName;
        var parkName = ExtractParkName(displayName, normalizedFieldKey);
        if (displayName.Contains('>', StringComparison.Ordinal))
        {
            var parts = displayName.Split('>', 2, StringSplitOptions.TrimEntries);
            parkName = FirstNonBlank(parts.ElementAtOrDefault(0), parkName);
            fieldName = FirstNonBlank(parts.ElementAtOrDefault(1), displayName);
        }

        return
            !string.Equals((slot.GetString("GameDate") ?? "").Trim(), block.Date, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals((slot.GetString("StartTime") ?? "").Trim(), block.StartTime, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals((slot.GetString("EndTime") ?? "").Trim(), block.EndTime, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(NormalizeFieldKey(slot.GetString("FieldKey")), normalizedFieldKey, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals((slot.GetString("DisplayName") ?? "").Trim(), displayName, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals((slot.GetString("FieldName") ?? "").Trim(), fieldName, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals((slot.GetString("ParkName") ?? "").Trim(), parkName, StringComparison.OrdinalIgnoreCase) ||
            !SlotEntityUtil.ReadBool(slot, "IsAvailability", false) ||
            !string.Equals(SlotEntityUtil.ReadString(slot, "GameType"), "Availability", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(SlotEntityUtil.ReadString(slot, "AllocationSlotType"), "practice", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals((slot.GetString("PracticeBookingPolicy") ?? "").Trim(), block.BookingPolicy, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals((slot.GetString("PracticeSlotKey") ?? "").Trim(), block.PracticeSlotKey, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals((slot.GetString("PracticeSourceRecordId") ?? "").Trim(), block.LiveRecordId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals((slot.GetString("PracticeSeasonLabel") ?? "").Trim(), block.SeasonLabel, StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveSeasonLabel(string? seasonLabel, IReadOnlyList<string> seasons)
    {
        var requested = (seasonLabel ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(requested))
        {
            return requested;
        }

        return seasons.FirstOrDefault() ?? "";
    }

    private static List<CanonicalFieldOptionDto> BuildCanonicalFieldOptions(IReadOnlyCollection<FieldInventoryLiveRecordEntity> liveRecords)
    {
        return liveRecords
            .Where(record => !string.IsNullOrWhiteSpace(record.FieldId))
            .GroupBy(record => NormalizeFieldKey(record.FieldId), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var first = group.First();
                var displayName = FirstNonBlank(first.FieldName, first.RawFieldName, group.Key);
                var fieldName = displayName.Contains('>', StringComparison.Ordinal)
                    ? FirstNonBlank(displayName.Split('>', 2, StringSplitOptions.TrimEntries).ElementAtOrDefault(1), displayName)
                    : displayName;
                var parkName = ExtractParkName(displayName, group.Key);
                return new CanonicalFieldOptionDto(group.Key, displayName, fieldName, parkName);
            })
            .OrderBy(option => option.CanonicalFieldName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static DivisionMatch ResolveDivision(
        string? rawDivision,
        IReadOnlyCollection<DivisionOption> divisions,
        IReadOnlyCollection<FieldInventoryDivisionAliasEntity> aliases)
    {
        var raw = (rawDivision ?? "").Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new DivisionMatch(null, null);
        }

        var normalized = NormalizeLookupKey(raw);
        var alias = aliases.FirstOrDefault(item =>
            string.Equals(item.NormalizedLookupKey, normalized, StringComparison.OrdinalIgnoreCase));
        if (alias is not null)
        {
            return new DivisionMatch(alias.CanonicalDivisionCode, alias.CanonicalDivisionName);
        }

        var exact = divisions.FirstOrDefault(division =>
            string.Equals(division.Code, raw, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(division.Name, raw, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(NormalizeLookupKey(division.Name), normalized, StringComparison.OrdinalIgnoreCase));
        return exact is null
            ? new DivisionMatch(null, null)
            : new DivisionMatch(exact.Code, exact.Name);
    }

    private static TeamMatch ResolveTeam(
        string? rawTeam,
        string? divisionCode,
        IReadOnlyCollection<TeamOption> teams,
        IReadOnlyCollection<FieldInventoryTeamAliasEntity> aliases)
    {
        var raw = (rawTeam ?? "").Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new TeamMatch(null, null);
        }

        var normalized = NormalizeLookupKey(raw);
        var scopedAlias = aliases.FirstOrDefault(item =>
            string.Equals(item.CanonicalDivisionCode, divisionCode ?? "", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.NormalizedLookupKey, normalized, StringComparison.OrdinalIgnoreCase));
        if (scopedAlias is not null)
        {
            return new TeamMatch(scopedAlias.CanonicalTeamId, scopedAlias.CanonicalTeamName);
        }

        var candidates = teams
            .Where(team => string.IsNullOrWhiteSpace(divisionCode) || string.Equals(team.DivisionCode, divisionCode, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var exact = candidates.FirstOrDefault(team =>
            string.Equals(team.TeamId, raw, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(team.TeamName, raw, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(NormalizeLookupKey(team.TeamName), normalized, StringComparison.OrdinalIgnoreCase));
        return exact is null
            ? new TeamMatch(null, null)
            : new TeamMatch(exact.TeamId, exact.TeamName);
    }

    private static PolicyDecision ResolvePolicy(
        FieldInventoryLiveRecordEntity record,
        IReadOnlyCollection<FieldInventoryGroupPolicyEntity> groupPolicies)
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
            var saved = groupPolicies.FirstOrDefault(item =>
                string.Equals(item.NormalizedLookupKey, normalizedGroup, StringComparison.OrdinalIgnoreCase));
            if (saved is not null)
            {
                return new PolicyDecision(NormalizeBookingPolicy(saved.BookingPolicy), $"Mapped from group '{rawGroup}'.", true);
            }
        }

        if (HasPonytailSignal(record))
        {
            return new PolicyDecision(FieldInventoryPracticeBookingPolicies.AutoApprove, "Ponytail-assigned space auto-approves coach requests.", true);
        }

        if (string.IsNullOrWhiteSpace(record.AssignedGroup) &&
            string.IsNullOrWhiteSpace(record.AssignedDivision) &&
            string.IsNullOrWhiteSpace(record.AssignedTeamOrEvent))
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
            NormalizeLookupKey(record.SourceValue),
        };

        return signals.Any(signal =>
            !string.IsNullOrWhiteSpace(signal) &&
            (signal.Contains("ponytail", StringComparison.OrdinalIgnoreCase) ||
             signal.Contains("ponytails", StringComparison.OrdinalIgnoreCase) ||
             signal.Contains("pypractice", StringComparison.OrdinalIgnoreCase) ||
             signal == "py"));
    }

    private static string ReadMembershipValue(TableEntity? membership, string primary, string legacy)
    {
        return (membership?.GetString(primary) ?? membership?.GetString(legacy) ?? "").Trim();
    }

    private static string NormalizeLookupKey(string? value)
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
    {
        return NormalizeBookingPolicy(bookingPolicy) switch
        {
            FieldInventoryPracticeBookingPolicies.AutoApprove => "Auto-approve",
            FieldInventoryPracticeBookingPolicies.CommissionerReview => "Commissioner review",
            _ => "Not requestable",
        };
    }

    private static bool TryParseMinutes(string? value, out int minutes)
    {
        minutes = 0;
        return !string.IsNullOrWhiteSpace(value) && TimeUtil.TryParseMinutes(value.Trim(), out minutes);
    }

    private static string FormatMinutes(int minutes)
    {
        var clamped = Math.Max(0, minutes);
        return $"{clamped / 60:D2}:{clamped % 60:D2}";
    }

    private static string WeekdayFromIso(string? isoDate)
    {
        return DateOnly.TryParse((isoDate ?? "").Trim(), out var parsed)
            ? parsed.DayOfWeek.ToString()
            : "";
    }

    private static string BuildIdentity(string division, string fieldKey, string gameDate, string startTime, string endTime)
    {
        return SlotKeyUtil.BuildIdentity(division, fieldKey, gameDate, startTime, endTime);
    }

    private static string NormalizeFieldKey(string? fieldKey)
    {
        return SlotKeyUtil.NormalizeFieldKey(fieldKey ?? "");
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

    private static string ExtractParkName(string? displayName, string? fieldKey = null)
    {
        var label = (displayName ?? "").Trim();
        if (label.Contains('>', StringComparison.Ordinal))
        {
            return label.Split('>', 2, StringSplitOptions.TrimEntries).ElementAtOrDefault(0) ?? "";
        }

        if (SlotKeyUtil.TrySplitFieldKey(fieldKey, out var parkCode, out _))
        {
            return parkCode;
        }

        return "";
    }

    private sealed class Bundle
    {
        public string LeagueId { get; set; } = "";
        public string SeasonLabel { get; set; } = "";
        public List<FieldInventoryPracticeSeasonOptionDto> SeasonOptions { get; set; } = [];
        public List<FieldInventoryPracticeAdminRowDto> AdminRows { get; set; } = [];
        public List<PracticeBlockCandidate> Blocks { get; set; } = [];
        public List<FieldInventoryPracticeRequestDto> Requests { get; set; } = [];
        public List<CanonicalFieldOptionDto> CanonicalFields { get; set; } = [];
        public List<CanonicalDivisionOptionDto> CanonicalDivisions { get; set; } = [];
        public List<CanonicalTeamOptionDto> CanonicalTeams { get; set; } = [];
    }

    private sealed record DivisionOption(string Code, string Name);
    private sealed record TeamOption(string DivisionCode, string TeamId, string TeamName);
    private sealed record DivisionMatch(string? Code, string? Name);
    private sealed record TeamMatch(string? TeamId, string? TeamName);
    private sealed record PolicyDecision(string BookingPolicy, string Reason, bool IsActionable);
    private sealed record MembershipContext(string Role, bool IsLeagueAdminOrGlobalAdmin, string TeamDivision, string TeamId, string? TeamName);
    private sealed record AlignedRecord(
        string SeasonLabel,
        FieldInventoryLiveRecordEntity Record,
        DivisionMatch Division,
        TeamMatch Team,
        PolicyDecision Policy,
        List<string> MappingIssues);

    private sealed class PracticeBlockCandidate
    {
        public string LeagueId { get; set; } = "";
        public string PracticeSlotKey { get; set; } = "";
        public string SeasonLabel { get; set; } = "";
        public string LiveRecordId { get; set; } = "";
        public string Date { get; set; } = "";
        public string DayOfWeek { get; set; } = "";
        public string StartTime { get; set; } = "";
        public string EndTime { get; set; } = "";
        public int SlotDurationMinutes { get; set; }
        public string FieldId { get; set; } = "";
        public string FieldName { get; set; } = "";
        public string BookingPolicy { get; set; } = FieldInventoryPracticeBookingPolicies.NotRequestable;
        public string BookingPolicyReason { get; set; } = "";
        public string? AssignedGroup { get; set; }
        public string? AssignedDivision { get; set; }
        public string? AssignedTeamOrEvent { get; set; }
        public string CanonicalDivisionCode { get; set; } = "";
        public string? CanonicalDivisionName { get; set; }
        public string? CanonicalTeamId { get; set; }
        public string? CanonicalTeamName { get; set; }
        public bool BaseRequestable { get; set; }
        public List<string> MappingIssues { get; set; } = [];
        public string DeterministicSlotId { get; set; } = "";
        public string EffectiveSlotId { get; set; } = "";
        public string NormalizationState { get; set; } = "blocked";
        public List<string> NormalizationIssues { get; set; } = [];
        public bool NeedsSlotMetadataSync { get; set; }
        public TableEntity? ExactSlot { get; set; }
        public List<TableEntity> OverlapSlots { get; set; } = [];
        public bool IsAvailable { get; set; }
        public List<string> PendingTeamIds { get; set; } = [];
        public bool Shareable { get; set; } = true;
        public int MaxTeamsPerBooking { get; set; } = 2;
        public List<string> ReservedTeamIds { get; set; } = [];
        public List<string> PendingShareTeamIds { get; set; } = [];
    }
}
