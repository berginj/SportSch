using System.Globalization;
using System.Net;
using Azure.Data.Tables;
using GameSwap.Functions.Repositories;
using GameSwap.Functions.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;

namespace GameSwap.Functions.Functions;

public class DashboardFunctions
{
    private readonly IAccessRequestRepository _accessRequestRepo;
    private readonly IDivisionRepository _divisionRepo;
    private readonly IMembershipRepository _membershipRepo;
    private readonly ISlotRepository _slotRepo;
    private readonly ITeamRepository _teamRepo;
    private readonly TableServiceClient _tableService;
    private readonly ILogger _log;

    public DashboardFunctions(
        IAccessRequestRepository accessRequestRepo,
        IDivisionRepository divisionRepo,
        IMembershipRepository membershipRepo,
        ISlotRepository slotRepo,
        ITeamRepository teamRepo,
        TableServiceClient tableService,
        ILoggerFactory loggerFactory)
    {
        _accessRequestRepo = accessRequestRepo;
        _divisionRepo = divisionRepo;
        _membershipRepo = membershipRepo;
        _slotRepo = slotRepo;
        _teamRepo = teamRepo;
        _tableService = tableService;
        _log = loggerFactory.CreateLogger<DashboardFunctions>();
    }

    public record AdminDashboardDto(
        int pendingRequests,
        int unassignedCoaches,
        int totalCoaches,
        int scheduleCoverage,
        int upcomingGames,
        int totalSlots,
        int confirmedSlots,
        int openSlots,
        int divisions);

    public record ContactDto(string name, string email, string phone);

    public record CoachTeamDto(
        string teamId,
        string division,
        string name,
        ContactDto? primaryContact);

    public record CoachDashboardGameDto(
        string slotId,
        string gameDate,
        string startTime,
        string endTime,
        string displayName,
        string fieldKey,
        string homeTeamId,
        string awayTeamId,
        string offeringTeamId,
        string confirmedTeamId,
        string status,
        string gameType);

    public record CoachDashboardDto(
        CoachTeamDto? team,
        List<CoachDashboardGameDto> upcomingGames,
        int openOffersInDivision,
        int myOpenOffers);

    [Function("GetAdminDashboard")]
    [OpenApiOperation(operationId: "GetAdminDashboard", tags: new[] { "Dashboard" }, Summary = "Get admin dashboard summary", Description = "Returns summary metrics for the current league admin dashboard.")]
    [OpenApiSecurity("league_id_header", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "x-league-id")]
    public async Task<HttpResponseData> GetAdminDashboard(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "admin/dashboard")] HttpRequestData req)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            await ApiGuards.RequireLeagueAdminAsync(_tableService, me.UserId, leagueId);

            var pendingRequests = (await _accessRequestRepo.QueryAccessRequestsByLeagueAsync(
                leagueId,
                Constants.Status.AccessRequestPending)).Count;

            var memberships = await _membershipRepo.GetLeagueMembershipsAsync(leagueId);
            var divisions = await _divisionRepo.QueryDivisionsAsync(leagueId);
            var slots = await QueryAllSlotsAsync(new SlotQueryFilter
            {
                LeagueId = leagueId,
                Statuses = new List<string> { Constants.Status.SlotOpen, Constants.Status.SlotConfirmed },
                ExcludeAvailability = true,
                PageSize = 250
            });

            var coaches = memberships
                .Where(IsCoachMembership)
                .ToList();

            var today = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var nextWeek = DateTime.UtcNow.AddDays(7).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var activeGameSlots = slots
                .Where(IsGameCapableSlot)
                .ToList();

            var confirmedSlots = activeGameSlots.Count(slot =>
                string.Equals(ReadString(slot, "Status"), Constants.Status.SlotConfirmed, StringComparison.OrdinalIgnoreCase));
            var openSlots = activeGameSlots.Count(slot =>
                string.Equals(ReadString(slot, "Status"), Constants.Status.SlotOpen, StringComparison.OrdinalIgnoreCase));
            var totalSlots = activeGameSlots.Count;
            var scheduleCoverage = totalSlots > 0
                ? (int)Math.Round((double)(confirmedSlots * 100) / totalSlots, MidpointRounding.AwayFromZero)
                : 0;
            var upcomingGames = activeGameSlots.Count(slot =>
                string.Equals(ReadString(slot, "Status"), Constants.Status.SlotConfirmed, StringComparison.OrdinalIgnoreCase) &&
                string.CompareOrdinal(ReadString(slot, "GameDate"), today) >= 0 &&
                string.CompareOrdinal(ReadString(slot, "GameDate"), nextWeek) <= 0);

            var response = new AdminDashboardDto(
                pendingRequests: pendingRequests,
                unassignedCoaches: coaches.Count(coach => string.IsNullOrWhiteSpace(ReadString(coach, "TeamId"))),
                totalCoaches: coaches.Count,
                scheduleCoverage: scheduleCoverage,
                upcomingGames: upcomingGames,
                totalSlots: totalSlots,
                confirmedSlots: confirmedSlots,
                openSlots: openSlots,
                divisions: divisions.Count);

            return ApiResponses.Ok(req, response);
        }
        catch (ApiGuards.HttpError ex)
        {
            return ApiResponses.FromHttpError(req, ex);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "GetAdminDashboard failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, ErrorCodes.INTERNAL_ERROR, "An unexpected error occurred");
        }
    }

    [Function("GetCoachDashboard")]
    [OpenApiOperation(operationId: "GetCoachDashboard", tags: new[] { "Dashboard" }, Summary = "Get coach dashboard summary", Description = "Returns summary metrics and upcoming games for the current coach dashboard.")]
    [OpenApiSecurity("league_id_header", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "x-league-id")]
    public async Task<HttpResponseData> GetCoachDashboard(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "coach/dashboard")] HttpRequestData req)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            await ApiGuards.RequireMemberAsync(_tableService, me.UserId, leagueId);

            var membership = await _membershipRepo.GetMembershipAsync(me.UserId, leagueId);
            if (!IsCoachMembership(membership))
            {
                return ApiResponses.Error(req, HttpStatusCode.Forbidden, ErrorCodes.FORBIDDEN, "Coach access is required for this dashboard.");
            }

            var division = ReadString(membership, "Division");
            var teamId = ReadString(membership, "TeamId");
            if (string.IsNullOrWhiteSpace(division) || string.IsNullOrWhiteSpace(teamId))
            {
                return ApiResponses.Ok(req, new CoachDashboardDto(
                    team: null,
                    upcomingGames: new List<CoachDashboardGameDto>(),
                    openOffersInDivision: 0,
                    myOpenOffers: 0));
            }

            var team = await _teamRepo.GetTeamAsync(leagueId, division, teamId);
            var slots = await QueryAllSlotsAsync(new SlotQueryFilter
            {
                LeagueId = leagueId,
                Division = division,
                Statuses = new List<string> { Constants.Status.SlotOpen, Constants.Status.SlotConfirmed },
                ExcludeAvailability = true,
                FromDate = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ToDate = DateTime.UtcNow.AddDays(30).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                PageSize = 250
            });

            var upcomingGames = slots
                .Where(IsGameCapableSlot)
                .Where(slot => string.Equals(ReadString(slot, "Status"), Constants.Status.SlotConfirmed, StringComparison.OrdinalIgnoreCase))
                .Where(slot => TeamAppearsInSlot(slot, teamId))
                .OrderBy(slot => ReadString(slot, "GameDate"))
                .ThenBy(slot => ReadString(slot, "StartTime"))
                .Take(5)
                .Select(MapCoachDashboardGame)
                .ToList();

            var openOffersInDivision = slots.Count(slot =>
                IsOpenGameOffer(slot) &&
                !string.Equals(ReadString(slot, "OfferingTeamId"), teamId, StringComparison.OrdinalIgnoreCase));
            var myOpenOffers = slots.Count(slot =>
                IsOpenGameOffer(slot) &&
                string.Equals(ReadString(slot, "OfferingTeamId"), teamId, StringComparison.OrdinalIgnoreCase));

            var response = new CoachDashboardDto(
                team: MapCoachDashboardTeam(division, teamId, team),
                upcomingGames: upcomingGames,
                openOffersInDivision: openOffersInDivision,
                myOpenOffers: myOpenOffers);

            return ApiResponses.Ok(req, response);
        }
        catch (ApiGuards.HttpError ex)
        {
            return ApiResponses.FromHttpError(req, ex);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "GetCoachDashboard failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, ErrorCodes.INTERNAL_ERROR, "An unexpected error occurred");
        }
    }

    private async Task<List<TableEntity>> QueryAllSlotsAsync(SlotQueryFilter filter)
    {
        var items = new List<TableEntity>();
        var seenTokens = new HashSet<string>(StringComparer.Ordinal);
        string? continuationToken = null;

        while (true)
        {
            var page = await _slotRepo.QuerySlotsAsync(filter, continuationToken);
            items.AddRange(page.Items);

            if (string.IsNullOrWhiteSpace(page.ContinuationToken) || !seenTokens.Add(page.ContinuationToken))
            {
                break;
            }

            continuationToken = page.ContinuationToken;
        }

        return items;
    }

    private static bool IsCoachMembership(TableEntity? membership)
    {
        return string.Equals(ReadString(membership, "Role"), Constants.Roles.Coach, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsGameCapableSlot(TableEntity slot)
    {
        if (ReadBool(slot, "IsAvailability")) return false;
        return !string.Equals(ReadString(slot, "GameType"), "practice", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsOpenGameOffer(TableEntity slot)
    {
        if (!IsGameCapableSlot(slot)) return false;
        if (!string.Equals(ReadString(slot, "Status"), Constants.Status.SlotOpen, StringComparison.OrdinalIgnoreCase)) return false;
        if (string.Equals(ReadString(slot, "GameType"), "request", StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    private static bool TeamAppearsInSlot(TableEntity slot, string teamId)
    {
        return string.Equals(ReadString(slot, "OfferingTeamId"), teamId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(ReadString(slot, "ConfirmedTeamId"), teamId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(ReadString(slot, "HomeTeamId"), teamId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(ReadString(slot, "AwayTeamId"), teamId, StringComparison.OrdinalIgnoreCase);
    }

    private static CoachTeamDto MapCoachDashboardTeam(string division, string teamId, TableEntity? team)
    {
        var name = ReadString(team, "Name");
        return new CoachTeamDto(
            teamId: teamId,
            division: division,
            name: string.IsNullOrWhiteSpace(name) ? teamId : name,
            primaryContact: team is null
                ? null
                : new ContactDto(
                    ReadString(team, "PrimaryContactName"),
                    ReadString(team, "PrimaryContactEmail"),
                    ReadString(team, "PrimaryContactPhone")));
    }

    private static CoachDashboardGameDto MapCoachDashboardGame(TableEntity slot)
    {
        return new CoachDashboardGameDto(
            slotId: slot.RowKey,
            gameDate: ReadString(slot, "GameDate"),
            startTime: ReadString(slot, "StartTime"),
            endTime: ReadString(slot, "EndTime"),
            displayName: ReadString(slot, "DisplayName"),
            fieldKey: ReadString(slot, "FieldKey"),
            homeTeamId: ReadString(slot, "HomeTeamId"),
            awayTeamId: ReadString(slot, "AwayTeamId"),
            offeringTeamId: ReadString(slot, "OfferingTeamId"),
            confirmedTeamId: ReadString(slot, "ConfirmedTeamId"),
            status: ReadString(slot, "Status", Constants.Status.SlotOpen),
            gameType: ReadString(slot, "GameType"));
    }

    private static string ReadString(TableEntity? entity, string key, string defaultValue = "")
    {
        if (entity is null) return defaultValue;
        if (!entity.TryGetValue(key, out var value) || value is null) return defaultValue;
        var text = value.ToString();
        return string.IsNullOrWhiteSpace(text) ? defaultValue : text.Trim();
    }

    private static bool ReadBool(TableEntity? entity, string key)
    {
        if (entity is null) return false;
        if (!entity.TryGetValue(key, out var value) || value is null) return false;
        if (value is bool boolValue) return boolValue;
        return bool.TryParse(value.ToString(), out var parsed) && parsed;
    }
}
