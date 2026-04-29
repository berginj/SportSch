using System.Net;
using GameSwap.Functions.Repositories;
using GameSwap.Functions.Services;
using GameSwap.Functions.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace GameSwap.Functions.Functions;

/// <summary>
/// Azure Functions for umpire assignment operations.
/// </summary>
public class UmpireAssignmentFunctions
{
    private readonly IUmpireAssignmentService _assignmentService;
    private readonly Azure.Data.Tables.TableServiceClient _tableService;
    private readonly IMembershipRepository _membershipRepo;
    private readonly ISlotRepository _slotRepo;
    private readonly ILogger _log;

    public UmpireAssignmentFunctions(
        IUmpireAssignmentService assignmentService,
        Azure.Data.Tables.TableServiceClient tableService,
        IMembershipRepository membershipRepo,
        ISlotRepository slotRepo,
        ILoggerFactory loggerFactory)
    {
        _assignmentService = assignmentService;
        _tableService = tableService;
        _membershipRepo = membershipRepo;
        _slotRepo = slotRepo;
        _log = loggerFactory.CreateLogger<UmpireAssignmentFunctions>();
    }

    [Function("AssignUmpireToGame")]
    public async Task<HttpResponseData> AssignUmpire(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "games/{division}/{slotId}/umpire-assignments")] HttpRequestData req,
        string division,
        string slotId)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);

            // Authorization: LeagueAdmin only
            await ApiGuards.RequireLeagueAdminAsync(_tableService, me.UserId, leagueId);

            var body = await HttpUtil.ReadJsonAsync<AssignUmpireRequestDto>(req);

            var assignRequest = new AssignUmpireRequest
            {
                LeagueId = leagueId,
                Division = division,
                SlotId = slotId,
                UmpireUserId = body?.umpireUserId ?? throw new ApiGuards.HttpError(400, ErrorCodes.MISSING_REQUIRED_FIELD, "umpireUserId is required"),
                Position = body.position,
                SendNotification = body.sendNotification ?? true
            };

            var context = new CorrelationContext
            {
                UserId = me.UserId,
                UserEmail = me.Email,
                LeagueId = leagueId,
                CorrelationId = req.FunctionContext.InvocationId.ToString()
            };

            var result = await _assignmentService.AssignUmpireToGameAsync(assignRequest, context);

            return ApiResponses.Ok(req, result);
        }
        catch (ApiGuards.HttpError ex) { return ApiResponses.FromHttpError(req, ex); }
        catch (Exception ex)
        {
            _log.LogError(ex, "AssignUmpireToGame failed for {Division}/{SlotId}", division, slotId);
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, ErrorCodes.INTERNAL_ERROR, "Internal Server Error");
        }
    }

    [Function("GetGameUmpireAssignments")]
    public async Task<HttpResponseData> GetGameAssignments(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "games/{division}/{slotId}/umpire-assignments")] HttpRequestData req,
        string division,
        string slotId)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);

            // Authorization: League member required
            await ApiGuards.RequireMemberAsync(_tableService, me.UserId, leagueId);

            // Additional check for coaches: must be on one of the teams in this game
            var isAdmin = await IsLeagueAdminAsync(me.UserId, leagueId);
            if (!isAdmin)
            {
                var membership = await _membershipRepo.GetMembershipAsync(me.UserId, leagueId);
                var role = (membership?.GetString("Role") ?? "").Trim();

                if (string.Equals(role, Constants.Roles.Coach, StringComparison.OrdinalIgnoreCase))
                {
                    // Verify coach is on one of the teams in this game
                    var game = await _slotRepo.GetSlotAsync(leagueId, division, slotId);
                    if (game == null)
                    {
                        return ApiResponses.Error(req, HttpStatusCode.NotFound, ErrorCodes.SLOT_NOT_FOUND, "Game not found");
                    }

                    var coachTeam = (membership?.GetString("TeamId") ?? membership?.GetString("CoachTeamId") ?? "").Trim();
                    var homeTeam = (game.GetString("HomeTeamId") ?? "").Trim();
                    var awayTeam = (game.GetString("AwayTeamId") ?? "").Trim();
                    var offeringTeam = (game.GetString("OfferingTeamId") ?? "").Trim();
                    var confirmedTeam = (game.GetString("ConfirmedTeamId") ?? "").Trim();

                    var isInvolvedTeam =
                        string.Equals(coachTeam, homeTeam, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(coachTeam, awayTeam, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(coachTeam, offeringTeam, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(coachTeam, confirmedTeam, StringComparison.OrdinalIgnoreCase);

                    if (!isInvolvedTeam)
                    {
                        return ApiResponses.Error(req, HttpStatusCode.Forbidden, ErrorCodes.FORBIDDEN,
                            "Coaches can only view umpire assignments for their own team's games");
                    }
                }
            }

            var result = await _assignmentService.GetGameAssignmentsAsync(leagueId, division, slotId);

            return ApiResponses.Ok(req, result);
        }
        catch (ApiGuards.HttpError ex) { return ApiResponses.FromHttpError(req, ex); }
        catch (Exception ex)
        {
            _log.LogError(ex, "GetGameAssignments failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, ErrorCodes.INTERNAL_ERROR, "Internal Server Error");
        }
    }

    private async Task<bool> IsLeagueAdminAsync(string userId, string leagueId)
    {
        if (await _membershipRepo.IsGlobalAdminAsync(userId))
            return true;

        var membership = await _membershipRepo.GetMembershipAsync(userId, leagueId);
        var role = (membership?.GetString("Role") ?? "").Trim();
        return string.Equals(role, Constants.Roles.LeagueAdmin, StringComparison.OrdinalIgnoreCase);
    }

    [Function("UpdateAssignmentStatus")]
    public async Task<HttpResponseData> UpdateStatus(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "umpire-assignments/{assignmentId}/status")] HttpRequestData req,
        string assignmentId)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);

            var body = await HttpUtil.ReadJsonAsync<UpdateStatusRequestDto>(req);

            var context = new CorrelationContext
            {
                UserId = me.UserId,
                UserEmail = me.Email,
                LeagueId = leagueId,
                CorrelationId = req.FunctionContext.InvocationId.ToString()
            };

            var result = await _assignmentService.UpdateAssignmentStatusAsync(
                assignmentId,
                body?.status ?? throw new ApiGuards.HttpError(400, ErrorCodes.MISSING_REQUIRED_FIELD, "status is required"),
                body.declineReason,
                context);

            return ApiResponses.Ok(req, result);
        }
        catch (ApiGuards.HttpError ex) { return ApiResponses.FromHttpError(req, ex); }
        catch (Exception ex)
        {
            _log.LogError(ex, "UpdateAssignmentStatus failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, ErrorCodes.INTERNAL_ERROR, "Internal Server Error");
        }
    }

    [Function("RemoveUmpireAssignment")]
    public async Task<HttpResponseData> RemoveAssignment(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "umpire-assignments/{assignmentId}")] HttpRequestData req,
        string assignmentId)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);

            // Authorization: LeagueAdmin only
            await ApiGuards.RequireLeagueAdminAsync(_tableService, me.UserId, leagueId);

            var context = new CorrelationContext
            {
                UserId = me.UserId,
                UserEmail = me.Email,
                LeagueId = leagueId,
                CorrelationId = req.FunctionContext.InvocationId.ToString()
            };

            await _assignmentService.RemoveAssignmentAsync(assignmentId, context);

            return ApiResponses.Ok(req, new { message = "Umpire assignment removed", assignmentId });
        }
        catch (ApiGuards.HttpError ex) { return ApiResponses.FromHttpError(req, ex); }
        catch (Exception ex)
        {
            _log.LogError(ex, "RemoveAssignment failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, ErrorCodes.INTERNAL_ERROR, "Internal Server Error");
        }
    }

    [Function("GetUnassignedGames")]
    public async Task<HttpResponseData> GetUnassignedGames(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "umpires/unassigned-games")] HttpRequestData req)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);

            // Authorization: LeagueAdmin only
            await ApiGuards.RequireLeagueAdminAsync(_tableService, me.UserId, leagueId);

            var division = ApiGuards.GetQueryParam(req, "division");
            var dateFrom = ApiGuards.GetQueryParam(req, "dateFrom");
            var dateTo = ApiGuards.GetQueryParam(req, "dateTo");

            var filter = new UnassignedGamesFilter
            {
                Division = division,
                DateFrom = dateFrom,
                DateTo = dateTo
            };

            var result = await _assignmentService.GetUnassignedGamesAsync(leagueId, filter);

            return ApiResponses.Ok(req, new { games = result, count = result.Count });
        }
        catch (ApiGuards.HttpError ex) { return ApiResponses.FromHttpError(req, ex); }
        catch (Exception ex)
        {
            _log.LogError(ex, "GetUnassignedGames failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, ErrorCodes.INTERNAL_ERROR, "Internal Server Error");
        }
    }

    [Function("CheckUmpireConflicts")]
    public async Task<HttpResponseData> CheckConflicts(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "umpires/check-conflicts")] HttpRequestData req)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);

            // Authorization: LeagueAdmin only (used during assignment workflow)
            await ApiGuards.RequireLeagueAdminAsync(_tableService, me.UserId, leagueId);

            var body = await HttpUtil.ReadJsonAsync<CheckConflictRequestDto>(req);

            if (string.IsNullOrWhiteSpace(body?.umpireUserId))
                throw new ApiGuards.HttpError(400, ErrorCodes.MISSING_REQUIRED_FIELD, "umpireUserId is required");

            if (string.IsNullOrWhiteSpace(body.gameDate))
                throw new ApiGuards.HttpError(400, ErrorCodes.MISSING_REQUIRED_FIELD, "gameDate is required");

            if (string.IsNullOrWhiteSpace(body.startTime) || string.IsNullOrWhiteSpace(body.endTime))
                throw new ApiGuards.HttpError(400, ErrorCodes.MISSING_REQUIRED_FIELD, "startTime and endTime are required");

            // Parse times
            if (!TimeUtil.TryParseMinutes(body.startTime, out var startMin))
                throw new ApiGuards.HttpError(400, ErrorCodes.INVALID_TIME, "Invalid startTime format");

            if (!TimeUtil.TryParseMinutes(body.endTime, out var endMin))
                throw new ApiGuards.HttpError(400, ErrorCodes.INVALID_TIME, "Invalid endTime format");

            var conflicts = await _assignmentService.CheckUmpireConflictsAsync(
                leagueId,
                body.umpireUserId,
                body.gameDate,
                startMin,
                endMin,
                body.excludeSlotId);

            return ApiResponses.Ok(req, new
            {
                hasConflict = conflicts.Any(),
                conflicts
            });
        }
        catch (ApiGuards.HttpError ex) { return ApiResponses.FromHttpError(req, ex); }
        catch (Exception ex)
        {
            _log.LogError(ex, "CheckConflicts failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, ErrorCodes.INTERNAL_ERROR, "Internal Server Error");
        }
    }

    [Function("FlagUmpireNoShow")]
    public async Task<HttpResponseData> FlagNoShow(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "umpire-assignments/{assignmentId}/no-show")] HttpRequestData req,
        string assignmentId)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);

            // Authorization: LeagueAdmin only
            await ApiGuards.RequireLeagueAdminAsync(_tableService, me.UserId, leagueId);

            var body = await HttpUtil.ReadJsonAsync<NoShowRequestDto>(req);

            var context = new CorrelationContext
            {
                UserId = me.UserId,
                UserEmail = me.Email,
                LeagueId = leagueId,
                CorrelationId = req.FunctionContext.InvocationId.ToString()
            };

            await _assignmentService.FlagNoShowAsync(assignmentId, body?.notes ?? "", context);

            return ApiResponses.Ok(req, new { message = "No-show flagged", assignmentId });
        }
        catch (ApiGuards.HttpError ex) { return ApiResponses.FromHttpError(req, ex); }
        catch (Exception ex)
        {
            _log.LogError(ex, "FlagNoShow failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, ErrorCodes.INTERNAL_ERROR, "Internal Server Error");
        }
    }

    // DTO classes for request bodies
    private record AssignUmpireRequestDto(string? umpireUserId, string? position, bool? sendNotification);
    private record UpdateStatusRequestDto(string? status, string? declineReason);
    private record CheckConflictRequestDto(string? umpireUserId, string? gameDate, string? startTime, string? endTime, string? excludeSlotId);
    private record NoShowRequestDto(string? notes);
}
