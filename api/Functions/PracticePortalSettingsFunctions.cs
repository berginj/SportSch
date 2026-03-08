using System.Net;
using Azure.Data.Tables;
using GameSwap.Functions.Repositories;
using GameSwap.Functions.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace GameSwap.Functions.Functions;

/// <summary>
/// Practice portal feature settings and readiness status.
/// Used by commissioners to enable one-off bookings and by coaches to understand gating.
/// </summary>
public class PracticePortalSettingsFunctions
{
    private readonly ILeagueRepository _leagueRepo;
    private readonly IDivisionRepository _divisionRepo;
    private readonly IMembershipRepository _membershipRepo;
    private readonly ITeamRepository _teamRepo;
    private readonly IPracticeRequestRepository _practiceRequestRepo;
    private readonly ILogger _log;

    private const string DivisionOneOffEnabledProp = "PracticeOneOffRequestsEnabled";

    public PracticePortalSettingsFunctions(
        ILeagueRepository leagueRepo,
        IDivisionRepository divisionRepo,
        IMembershipRepository membershipRepo,
        ITeamRepository teamRepo,
        IPracticeRequestRepository practiceRequestRepo,
        ILoggerFactory lf)
    {
        _leagueRepo = leagueRepo;
        _divisionRepo = divisionRepo;
        _membershipRepo = membershipRepo;
        _teamRepo = teamRepo;
        _practiceRequestRepo = practiceRequestRepo;
        _log = lf.CreateLogger<PracticePortalSettingsFunctions>();
    }

    public record MissingTeamDto(string teamId, string? name);
    public record DivisionRecurringCoverageDto(
        string division,
        int teamCount,
        int teamsWithApprovedRecurringPractice,
        bool allTeamsHaveRecurringPractice,
        List<MissingTeamDto> missingTeams);

    public record PracticePortalSettingsDto(
        bool recurringSelectionEnabled,
        bool oneOffRequestsEnabled,
        DivisionRecurringCoverageDto? divisionStatus);

    public record PatchPracticePortalSettingsReq(bool? oneOffRequestsEnabled, string? division);

    [Function("GetPracticePortalSettings")]
    public async Task<HttpResponseData> Get(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "practice-portal/settings")] HttpRequestData req)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            var division = (ApiGuards.GetQueryParam(req, "division") ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(division))
                ApiGuards.EnsureValidTableKeyPart("division", division);

            var isGlobalAdmin = await _membershipRepo.IsGlobalAdminAsync(me.UserId);
            var membership = await _membershipRepo.GetMembershipAsync(me.UserId, leagueId);
            if (!isGlobalAdmin && membership is null)
            {
                return ApiResponses.Error(req, HttpStatusCode.Forbidden, ErrorCodes.FORBIDDEN,
                    "Only league members can view practice portal settings.");
            }

            var league = await _leagueRepo.GetLeagueAsync(leagueId);
            if (league is null)
            {
                return ApiResponses.Error(req, HttpStatusCode.NotFound, "NOT_FOUND", $"league not found: {leagueId}");
            }

            var oneOffEnabled = false;
            DivisionRecurringCoverageDto? divisionStatus = null;
            if (!string.IsNullOrWhiteSpace(division))
            {
                oneOffEnabled = await ReadDivisionOneOffEnabledAsync(leagueId, division);
                divisionStatus = await BuildDivisionCoverageAsync(leagueId, division);
            }

            return ApiResponses.Ok(req, new PracticePortalSettingsDto(
                recurringSelectionEnabled: true,
                oneOffRequestsEnabled: oneOffEnabled,
                divisionStatus: divisionStatus));
        }
        catch (ApiGuards.HttpError ex)
        {
            return ApiResponses.FromHttpError(req, ex);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "GetPracticePortalSettings failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, ErrorCodes.INTERNAL_ERROR, "Internal Server Error");
        }
    }

    [Function("PatchPracticePortalSettings")]
    public async Task<HttpResponseData> Patch(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "practice-portal/settings")] HttpRequestData req)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);

            if (!await _membershipRepo.IsGlobalAdminAsync(me.UserId))
            {
                var membership = await _membershipRepo.GetMembershipAsync(me.UserId, leagueId);
                var role = (membership?.GetString("Role") ?? Constants.Roles.Viewer).Trim();
                if (!string.Equals(role, Constants.Roles.LeagueAdmin, StringComparison.OrdinalIgnoreCase))
                {
                    return ApiResponses.Error(req, HttpStatusCode.Forbidden, ErrorCodes.FORBIDDEN,
                        "Only league admins can update practice portal settings.");
                }
            }

            var body = await HttpUtil.ReadJsonAsync<PatchPracticePortalSettingsReq>(req);
            if (body is null)
            {
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, ErrorCodes.BAD_REQUEST, "Invalid JSON body");
            }

            var league = await _leagueRepo.GetLeagueAsync(leagueId);
            if (league is null)
            {
                return ApiResponses.Error(req, HttpStatusCode.NotFound, "NOT_FOUND", $"league not found: {leagueId}");
            }

            var division = (body.division ?? "").Trim();
            if (string.IsNullOrWhiteSpace(division))
            {
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, ErrorCodes.BAD_REQUEST,
                    "division is required when updating practice portal settings.");
            }
            ApiGuards.EnsureValidTableKeyPart("division", division);

            var divisionEntity = await _divisionRepo.GetDivisionAsync(leagueId, division);
            if (divisionEntity is null)
            {
                return ApiResponses.Error(req, HttpStatusCode.NotFound, "NOT_FOUND", $"division not found: {division}");
            }

            if (body.oneOffRequestsEnabled.HasValue)
            {
                divisionEntity[DivisionOneOffEnabledProp] = body.oneOffRequestsEnabled.Value;
            }
            divisionEntity["UpdatedUtc"] = DateTimeOffset.UtcNow;
            await _divisionRepo.UpdateDivisionAsync(divisionEntity);

            var divisionStatus = await BuildDivisionCoverageAsync(leagueId, division);

            return ApiResponses.Ok(req, new PracticePortalSettingsDto(
                recurringSelectionEnabled: true,
                oneOffRequestsEnabled: divisionEntity.GetBoolean(DivisionOneOffEnabledProp) ?? false,
                divisionStatus: divisionStatus));
        }
        catch (ApiGuards.HttpError ex)
        {
            return ApiResponses.FromHttpError(req, ex);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "PatchPracticePortalSettings failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, ErrorCodes.INTERNAL_ERROR, "Internal Server Error");
        }
    }

    private async Task<DivisionRecurringCoverageDto> BuildDivisionCoverageAsync(string leagueId, string division)
    {
        var teams = await _teamRepo.QueryTeamsByDivisionAsync(leagueId, division);
        var activeTeams = teams
            .Select(t => new MissingTeamDto(
                teamId: (t.GetString("TeamId") ?? t.RowKey).Trim(),
                name: (t.GetString("Name") ?? "").Trim()))
            .Where(t => !string.IsNullOrWhiteSpace(t.teamId))
            .OrderBy(t => t.name ?? t.teamId)
            .ThenBy(t => t.teamId)
            .ToList();

        var approvedRequests = await _practiceRequestRepo.QueryRequestsAsync(
            leagueId,
            status: "Approved",
            division: division,
            teamId: null,
            slotId: null);

        var teamsWithApproved = approvedRequests
            .Select(r => (r.GetString("TeamId") ?? "").Trim())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missingTeams = activeTeams
            .Where(t => !teamsWithApproved.Contains(t.teamId))
            .ToList();

        return new DivisionRecurringCoverageDto(
            division: division,
            teamCount: activeTeams.Count,
            teamsWithApprovedRecurringPractice: activeTeams.Count - missingTeams.Count,
            allTeamsHaveRecurringPractice: activeTeams.Count > 0 && missingTeams.Count == 0,
            missingTeams: missingTeams);
    }

    private async Task<bool> ReadDivisionOneOffEnabledAsync(string leagueId, string division)
    {
        var divisionEntity = await _divisionRepo.GetDivisionAsync(leagueId, division);
        if (divisionEntity is null) return false;
        return divisionEntity.GetBoolean(DivisionOneOffEnabledProp) ?? false;
    }
}
