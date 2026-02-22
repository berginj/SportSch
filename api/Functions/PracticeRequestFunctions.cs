using System.Net;
using Azure.Data.Tables;
using GameSwap.Functions.Repositories;
using GameSwap.Functions.Services;
using GameSwap.Functions.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Extensions.OpenApi.Extensions;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;

namespace GameSwap.Functions.Functions;

/// <summary>
/// Practice slot request/approval workflow.
/// Coaches request slots (1-3) and commissioners approve/reject.
/// </summary>
public class PracticeRequestFunctions
{
    private readonly IPracticeRequestService _practiceRequestService;
    private readonly ISlotRepository _slotRepo;
    private readonly ITeamRepository _teamRepo;
    private readonly IEmailService _emailService;
    private readonly ILogger _log;

    public PracticeRequestFunctions(
        IPracticeRequestService practiceRequestService,
        ISlotRepository slotRepo,
        ITeamRepository teamRepo,
        IEmailService emailService,
        ILoggerFactory lf)
    {
        _practiceRequestService = practiceRequestService;
        _slotRepo = slotRepo;
        _teamRepo = teamRepo;
        _emailService = emailService;
        _log = lf.CreateLogger<PracticeRequestFunctions>();
    }

    public record PracticeRequestDto(
        string requestId,
        string division,
        string teamId,
        string slotId,
        string status,
        string? reason,
        DateTimeOffset requestedUtc,
        DateTimeOffset? reviewedUtc,
        string? reviewedBy,
        SlotSummary? slot
    );

    public record SlotSummary(
        string slotId,
        string gameDate,
        string startTime,
        string endTime,
        string? displayName,
        string? fieldKey
    );

    public record CreatePracticeRequestReq(
        string division,
        string teamId,
        string slotId,
        string? reason
    );

    public record ReviewPracticeRequestReq(string? reason);

    private static PracticeRequestDto ToDto(TableEntity e, SlotSummary? slot = null)
    {
        return new PracticeRequestDto(
            requestId: e.RowKey,
            division: (e.GetString("Division") ?? "").Trim(),
            teamId: (e.GetString("TeamId") ?? "").Trim(),
            slotId: (e.GetString("SlotId") ?? "").Trim(),
            status: (e.GetString("Status") ?? "Pending").Trim(),
            reason: (e.GetString("Reason") ?? "").Trim(),
            requestedUtc: e.GetDateTimeOffset("RequestedUtc") ?? DateTimeOffset.MinValue,
            reviewedUtc: e.GetDateTimeOffset("ReviewedUtc"),
            reviewedBy: (e.GetString("ReviewedBy") ?? "").Trim(),
            slot: slot
        );
    }

    [Function("CreatePracticeRequest")]
    [OpenApiOperation(operationId: "CreatePracticeRequest", tags: new[] { "Practice Requests" },
        Summary = "Request practice slot",
        Description = "Coaches submit requests for practice slots (requires commissioner approval). Each team can request 1-3 slots.")]
    [OpenApiSecurity("league_id_header", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "x-league-id")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(CreatePracticeRequestReq),
        Required = true, Description = "Practice slot request details")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Created, contentType: "application/json",
        bodyType: typeof(PracticeRequestDto), Description = "Request created successfully")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.BadRequest, contentType: "application/json",
        bodyType: typeof(object), Description = "Invalid request or team already has 3 pending requests")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Forbidden, contentType: "application/json",
        bodyType: typeof(object), Description = "Only coaches can request practice slots")]
    public async Task<HttpResponseData> CreatePracticeRequest(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "practice-requests")] HttpRequestData req)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            var body = await HttpUtil.ReadJsonAsync<CreatePracticeRequestReq>(req);
            if (body is null)
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, ErrorCodes.BAD_REQUEST, "Invalid JSON body");

            var requestEntity = await _practiceRequestService.CreateRequestAsync(
                leagueId: leagueId,
                userId: me.UserId,
                division: body.division,
                teamId: body.teamId,
                slotId: body.slotId,
                reason: body.reason);

            SlotSummary? slotSummary = null;
            var division = (requestEntity.GetString("Division") ?? "").Trim();
            var slotId = (requestEntity.GetString("SlotId") ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(division) && !string.IsNullOrWhiteSpace(slotId))
            {
                var slot = await _slotRepo.GetSlotAsync(leagueId, division, slotId);
                slotSummary = ToSlotSummary(slot);
            }

            return ApiResponses.Ok(req, ToDto(requestEntity, slotSummary), HttpStatusCode.Created);
        }
        catch (ApiGuards.HttpError ex) { return ApiResponses.FromHttpError(req, ex); }
        catch (Exception ex)
        {
            _log.LogError(ex, "CreatePracticeRequest failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, ErrorCodes.INTERNAL_ERROR, "Internal Server Error");
        }
    }

    [Function("GetPracticeRequests")]
    [OpenApiOperation(operationId: "GetPracticeRequests", tags: new[] { "Practice Requests" },
        Summary = "Get practice requests",
        Description = "Retrieve practice slot requests. Coaches see their own team's requests. Admins see all requests, optionally filtered by status.")]
    [OpenApiSecurity("league_id_header", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "x-league-id")]
    [OpenApiParameter(name: "status", In = ParameterLocation.Query, Required = false, Type = typeof(string),
        Description = "Filter by status: Pending, Approved, Rejected")]
    [OpenApiParameter(name: "teamId", In = ParameterLocation.Query, Required = false, Type = typeof(string),
        Description = "Filter by team (admins only)")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json",
        bodyType: typeof(object), Description = "Practice requests retrieved successfully")]
    public async Task<HttpResponseData> GetPracticeRequests(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "practice-requests")] HttpRequestData req)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            var statusFilter = (ApiGuards.GetQueryParam(req, "status") ?? "").Trim();
            var teamIdFilter = (ApiGuards.GetQueryParam(req, "teamId") ?? "").Trim();

            var entities = await _practiceRequestService.QueryRequestsAsync(
                leagueId: leagueId,
                userId: me.UserId,
                statusFilter: statusFilter,
                teamIdFilter: teamIdFilter);

            var list = new List<PracticeRequestDto>();
            foreach (var e in entities)
            {
                var slotId = (e.GetString("SlotId") ?? "").Trim();
                var division = (e.GetString("Division") ?? "").Trim();
                SlotSummary? slotSummary = null;
                if (!string.IsNullOrWhiteSpace(slotId) && !string.IsNullOrWhiteSpace(division))
                {
                    try
                    {
                        var slot = await _slotRepo.GetSlotAsync(leagueId, division, slotId);
                        slotSummary = ToSlotSummary(slot);
                    }
                    catch
                    {
                        // Slot may have been deleted; keep request row visible.
                    }
                }
                list.Add(ToDto(e, slotSummary));
            }

            return ApiResponses.Ok(req, list);
        }
        catch (ApiGuards.HttpError ex) { return ApiResponses.FromHttpError(req, ex); }
        catch (Exception ex)
        {
            _log.LogError(ex, "GetPracticeRequests failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, ErrorCodes.INTERNAL_ERROR, "Internal Server Error");
        }
    }

    [Function("ApprovePracticeRequest")]
    [OpenApiOperation(operationId: "ApprovePracticeRequest", tags: new[] { "Practice Requests" },
        Summary = "Approve practice request",
        Description = "Approve a pending practice slot request. Only league admins can approve requests.")]
    [OpenApiSecurity("league_id_header", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "x-league-id")]
    [OpenApiParameter(name: "requestId", In = ParameterLocation.Path, Required = true, Type = typeof(string),
        Description = "Practice request ID")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(ReviewPracticeRequestReq),
        Required = false, Description = "Optional approval reason/notes")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json",
        bodyType: typeof(PracticeRequestDto), Description = "Request approved successfully")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Forbidden, contentType: "application/json",
        bodyType: typeof(object), Description = "Only league admins can approve requests")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.NotFound, contentType: "application/json",
        bodyType: typeof(object), Description = "Request not found")]
    public async Task<HttpResponseData> ApprovePracticeRequest(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "practice-requests/{requestId}/approve")]
        HttpRequestData req,
        string requestId)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            var body = await HttpUtil.ReadJsonAsync<ReviewPracticeRequestReq>(req);
            var reason = (body?.reason ?? "").Trim();

            var entity = await _practiceRequestService.ApproveRequestAsync(
                leagueId: leagueId,
                userId: me.UserId,
                requestId: (requestId ?? "").Trim(),
                reason: reason);

            await TrySendApprovedEmailAsync(leagueId, entity);
            return ApiResponses.Ok(req, ToDto(entity));
        }
        catch (ApiGuards.HttpError ex) { return ApiResponses.FromHttpError(req, ex); }
        catch (Exception ex)
        {
            _log.LogError(ex, "ApprovePracticeRequest failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, ErrorCodes.INTERNAL_ERROR, "Internal Server Error");
        }
    }

    [Function("RejectPracticeRequest")]
    [OpenApiOperation(operationId: "RejectPracticeRequest", tags: new[] { "Practice Requests" },
        Summary = "Reject practice request",
        Description = "Reject a pending practice slot request. Only league admins can reject requests.")]
    [OpenApiSecurity("league_id_header", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "x-league-id")]
    [OpenApiParameter(name: "requestId", In = ParameterLocation.Path, Required = true, Type = typeof(string),
        Description = "Practice request ID")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(ReviewPracticeRequestReq),
        Required = false, Description = "Optional rejection reason")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json",
        bodyType: typeof(PracticeRequestDto), Description = "Request rejected successfully")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Forbidden, contentType: "application/json",
        bodyType: typeof(object), Description = "Only league admins can reject requests")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.NotFound, contentType: "application/json",
        bodyType: typeof(object), Description = "Request not found")]
    public async Task<HttpResponseData> RejectPracticeRequest(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "practice-requests/{requestId}/reject")]
        HttpRequestData req,
        string requestId)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            var body = await HttpUtil.ReadJsonAsync<ReviewPracticeRequestReq>(req);
            var reason = (body?.reason ?? "").Trim();

            var entity = await _practiceRequestService.RejectRequestAsync(
                leagueId: leagueId,
                userId: me.UserId,
                requestId: (requestId ?? "").Trim(),
                reason: reason);

            await TrySendRejectedEmailAsync(leagueId, entity, reason);
            return ApiResponses.Ok(req, ToDto(entity));
        }
        catch (ApiGuards.HttpError ex) { return ApiResponses.FromHttpError(req, ex); }
        catch (Exception ex)
        {
            _log.LogError(ex, "RejectPracticeRequest failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, ErrorCodes.INTERNAL_ERROR, "Internal Server Error");
        }
    }

    private static SlotSummary? ToSlotSummary(TableEntity? slot)
    {
        if (slot is null) return null;
        return new SlotSummary(
            slotId: slot.RowKey,
            gameDate: (slot.GetString("GameDate") ?? "").Trim(),
            startTime: (slot.GetString("StartTime") ?? "").Trim(),
            endTime: (slot.GetString("EndTime") ?? "").Trim(),
            displayName: (slot.GetString("DisplayName") ?? "").Trim(),
            fieldKey: (slot.GetString("FieldKey") ?? "").Trim());
    }

    private async Task TrySendApprovedEmailAsync(string leagueId, TableEntity requestEntity)
    {
        try
        {
            var division = (requestEntity.GetString("Division") ?? "").Trim();
            var teamId = (requestEntity.GetString("TeamId") ?? "").Trim();
            var slotId = (requestEntity.GetString("SlotId") ?? "").Trim();
            if (string.IsNullOrWhiteSpace(division) || string.IsNullOrWhiteSpace(teamId) || string.IsNullOrWhiteSpace(slotId))
                return;

            var team = await _teamRepo.GetTeamAsync(leagueId, division, teamId);
            var teamName = (team?.GetString("Name") ?? teamId).Trim();
            var teamEmail = (team?.GetString("PrimaryContactEmail") ?? "").Trim();
            if (string.IsNullOrWhiteSpace(teamEmail))
                return;

            var slot = await _slotRepo.GetSlotAsync(leagueId, division, slotId);
            if (slot is null)
                return;

            await _emailService.SendPracticeRequestApprovedEmailAsync(
                to: teamEmail,
                leagueId: leagueId,
                teamName: teamName,
                gameDate: (slot.GetString("GameDate") ?? "").Trim(),
                startTime: (slot.GetString("StartTime") ?? "").Trim(),
                endTime: (slot.GetString("EndTime") ?? "").Trim(),
                field: (slot.GetString("DisplayName") ?? slot.GetString("FieldKey") ?? "TBD").Trim());
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to send practice request approved email for requestId {RequestId}", requestEntity.RowKey);
        }
    }

    private async Task TrySendRejectedEmailAsync(string leagueId, TableEntity requestEntity, string fallbackReason)
    {
        try
        {
            var division = (requestEntity.GetString("Division") ?? "").Trim();
            var teamId = (requestEntity.GetString("TeamId") ?? "").Trim();
            var slotId = (requestEntity.GetString("SlotId") ?? "").Trim();
            if (string.IsNullOrWhiteSpace(division) || string.IsNullOrWhiteSpace(teamId) || string.IsNullOrWhiteSpace(slotId))
                return;

            var team = await _teamRepo.GetTeamAsync(leagueId, division, teamId);
            var teamName = (team?.GetString("Name") ?? teamId).Trim();
            var teamEmail = (team?.GetString("PrimaryContactEmail") ?? "").Trim();
            if (string.IsNullOrWhiteSpace(teamEmail))
                return;

            var slot = await _slotRepo.GetSlotAsync(leagueId, division, slotId);
            if (slot is null)
                return;

            var resolvedReason = !string.IsNullOrWhiteSpace(fallbackReason)
                ? fallbackReason
                : ((requestEntity.GetString("ReviewReason") ?? "").Trim());
            if (string.IsNullOrWhiteSpace(resolvedReason))
                resolvedReason = "Slot no longer available";

            await _emailService.SendPracticeRequestRejectedEmailAsync(
                to: teamEmail,
                leagueId: leagueId,
                teamName: teamName,
                gameDate: (slot.GetString("GameDate") ?? "").Trim(),
                startTime: (slot.GetString("StartTime") ?? "").Trim(),
                endTime: (slot.GetString("EndTime") ?? "").Trim(),
                reason: resolvedReason);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to send practice request rejected email for requestId {RequestId}", requestEntity.RowKey);
        }
    }
}
