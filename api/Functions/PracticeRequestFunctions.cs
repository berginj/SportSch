using System.Net;
using Azure;
using Azure.Data.Tables;
using GameSwap.Functions.Repositories;
using GameSwap.Functions.Services;
using GameSwap.Functions.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Extensions.OpenApi.Extensions;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.OpenApi.Models;
using Microsoft.Extensions.Logging;

namespace GameSwap.Functions.Functions;

/// <summary>
/// Practice slot request/approval workflow.
/// Coaches REQUEST practice slots (1-3), commissioners APPROVE/REJECT.
/// </summary>
public class PracticeRequestFunctions
{
    private readonly TableServiceClient _tableService;
    private readonly IMembershipRepository _membershipRepo;
    private readonly ISlotRepository _slotRepo;
    private readonly IEmailService _emailService;
    private readonly ILogger _log;

    public PracticeRequestFunctions(
        TableServiceClient tableService,
        IMembershipRepository membershipRepo,
        ISlotRepository slotRepo,
        IEmailService emailService,
        ILoggerFactory lf)
    {
        _tableService = tableService;
        _membershipRepo = membershipRepo;
        _slotRepo = slotRepo;
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

    private static string PracticeRequestPk(string leagueId) => $"PRACTICEREQ|{leagueId}";

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

            // Authorization - coach or admin
            var membership = await _membershipRepo.GetMembershipAsync(me.UserId, leagueId);
            var role = (membership?.GetString("Role") ?? Constants.Roles.Viewer).Trim();
            var isAdmin = await _membershipRepo.IsGlobalAdminAsync(me.UserId) ||
                          string.Equals(role, Constants.Roles.LeagueAdmin, StringComparison.OrdinalIgnoreCase);
            var isCoach = string.Equals(role, Constants.Roles.Coach, StringComparison.OrdinalIgnoreCase);

            if (!isCoach && !isAdmin)
            {
                return ApiResponses.Error(req, HttpStatusCode.Forbidden, ErrorCodes.FORBIDDEN,
                    "Only coaches can request practice slots");
            }

            var body = await HttpUtil.ReadJsonAsync<CreatePracticeRequestReq>(req);
            if (body is null)
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "Invalid JSON body");

            var division = (body.division ?? "").Trim();
            var teamId = (body.teamId ?? "").Trim();
            var slotId = (body.slotId ?? "").Trim();

            if (string.IsNullOrWhiteSpace(division) || string.IsNullOrWhiteSpace(teamId) || string.IsNullOrWhiteSpace(slotId))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST",
                    "division, teamId, and slotId are required");

            // Check if slot exists and is available
            var slot = await _slotRepo.GetSlotAsync(leagueId, division, slotId);
            if (slot is null)
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "Slot not found");

            var slotStatus = (slot.GetString("Status") ?? "").Trim();
            if (!string.Equals(slotStatus, "Open", StringComparison.OrdinalIgnoreCase))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST",
                    "Slot is not available (status must be Open)");

            // Check team doesn't already have 3 pending/approved requests
            var client = await TableClients.GetTableAsync(_tableService, Constants.Tables.PracticeRequests);
            var pk = PracticeRequestPk(leagueId);
            var existingRequests = client.QueryAsync<TableEntity>(
                filter: $"PartitionKey eq '{pk}' and TeamId eq '{teamId}' and (Status eq 'Pending' or Status eq 'Approved')"
            );

            var count = 0;
            await foreach (var r in existingRequests)
            {
                count++;
                if (count >= 3)
                {
                    return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST",
                        "Team already has 3 pending/approved practice requests. Maximum is 3 slots per team.");
                }
            }

            // Check if team already requested this exact slot
            var duplicateCheck = client.QueryAsync<TableEntity>(
                filter: $"PartitionKey eq '{pk}' and TeamId eq '{teamId}' and SlotId eq '{slotId}' and (Status eq 'Pending' or Status eq 'Approved')"
            );
            await foreach (var _ in duplicateCheck)
            {
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST",
                    "Team already requested this practice slot");
            }

            // Create request
            var requestId = Guid.NewGuid().ToString();
            var entity = new TableEntity(pk, requestId)
            {
                ["LeagueId"] = leagueId,
                ["Division"] = division,
                ["TeamId"] = teamId,
                ["SlotId"] = slotId,
                ["Status"] = "Pending",
                ["Reason"] = (body.reason ?? "").Trim(),
                ["RequestedUtc"] = DateTimeOffset.UtcNow,
                ["RequestedBy"] = me.UserId
            };

            await client.AddEntityAsync(entity);

            var slotSummary = new SlotSummary(
                slotId: slot.RowKey,
                gameDate: (slot.GetString("GameDate") ?? "").Trim(),
                startTime: (slot.GetString("StartTime") ?? "").Trim(),
                endTime: (slot.GetString("EndTime") ?? "").Trim(),
                displayName: (slot.GetString("DisplayName") ?? "").Trim(),
                fieldKey: (slot.GetString("FieldKey") ?? "").Trim()
            );

            return ApiResponses.Ok(req, ToDto(entity, slotSummary), HttpStatusCode.Created);
        }
        catch (ApiGuards.HttpError ex) { return ApiResponses.FromHttpError(req, ex); }
        catch (Exception ex)
        {
            _log.LogError(ex, "CreatePracticeRequest failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
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

            // Authorization
            var membership = await _membershipRepo.GetMembershipAsync(me.UserId, leagueId);
            var role = (membership?.GetString("Role") ?? Constants.Roles.Viewer).Trim();
            var isAdmin = await _membershipRepo.IsGlobalAdminAsync(me.UserId) ||
                          string.Equals(role, Constants.Roles.LeagueAdmin, StringComparison.OrdinalIgnoreCase);
            var isCoach = string.Equals(role, Constants.Roles.Coach, StringComparison.OrdinalIgnoreCase);

            if (!isCoach && !isAdmin)
            {
                return ApiResponses.Error(req, HttpStatusCode.Forbidden, ErrorCodes.FORBIDDEN,
                    "Only coaches and admins can view practice requests");
            }

            var statusFilter = (ApiGuards.GetQueryParam(req, "status") ?? "").Trim();
            var teamIdFilter = (ApiGuards.GetQueryParam(req, "teamId") ?? "").Trim();

            // Coaches can only see their own team's requests
            if (isCoach && !isAdmin)
            {
                var coachTeamId = (membership?.GetString("TeamId") ?? "").Trim();
                if (string.IsNullOrWhiteSpace(coachTeamId))
                {
                    return ApiResponses.Ok(req, new List<PracticeRequestDto>());
                }
                teamIdFilter = coachTeamId;
            }

            var client = await TableClients.GetTableAsync(_tableService, Constants.Tables.PracticeRequests);
            var pk = PracticeRequestPk(leagueId);

            // Build filter
            var filter = $"PartitionKey eq '{pk}'";
            if (!string.IsNullOrWhiteSpace(statusFilter))
                filter += $" and Status eq '{statusFilter}'";
            if (!string.IsNullOrWhiteSpace(teamIdFilter))
                filter += $" and TeamId eq '{teamIdFilter}'";

            var entities = client.QueryAsync<TableEntity>(filter: filter);
            var list = new List<PracticeRequestDto>();

            await foreach (var e in entities)
            {
                // Load slot details
                var slotId = (e.GetString("SlotId") ?? "").Trim();
                var division = (e.GetString("Division") ?? "").Trim();
                SlotSummary? slotSummary = null;

                if (!string.IsNullOrWhiteSpace(slotId) && !string.IsNullOrWhiteSpace(division))
                {
                    try
                    {
                        var slot = await _slotRepo.GetSlotAsync(leagueId, division, slotId);
                        if (slot is not null)
                        {
                            slotSummary = new SlotSummary(
                                slotId: slot.RowKey,
                                gameDate: (slot.GetString("GameDate") ?? "").Trim(),
                                startTime: (slot.GetString("StartTime") ?? "").Trim(),
                                endTime: (slot.GetString("EndTime") ?? "").Trim(),
                                displayName: (slot.GetString("DisplayName") ?? "").Trim(),
                                fieldKey: (slot.GetString("FieldKey") ?? "").Trim()
                            );
                        }
                    }
                    catch
                    {
                        // Slot might have been deleted - continue without slot details
                    }
                }

                list.Add(ToDto(e, slotSummary));
            }

            return ApiResponses.Ok(req, list.OrderByDescending(x => x.requestedUtc));
        }
        catch (ApiGuards.HttpError ex) { return ApiResponses.FromHttpError(req, ex); }
        catch (Exception ex)
        {
            _log.LogError(ex, "GetPracticeRequests failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
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

            // Authorization - admin only
            if (!await _membershipRepo.IsGlobalAdminAsync(me.UserId))
            {
                var membership = await _membershipRepo.GetMembershipAsync(me.UserId, leagueId);
                var role = (membership?.GetString("Role") ?? Constants.Roles.Viewer).Trim();
                if (!string.Equals(role, Constants.Roles.LeagueAdmin, StringComparison.OrdinalIgnoreCase))
                {
                    return ApiResponses.Error(req, HttpStatusCode.Forbidden, ErrorCodes.FORBIDDEN,
                        "Only league admins can approve practice requests");
                }
            }

            var body = await HttpUtil.ReadJsonAsync<ReviewPracticeRequestReq>(req);
            var reason = (body?.reason ?? "").Trim();

            var client = await TableClients.GetTableAsync(_tableService, Constants.Tables.PracticeRequests);
            var pk = PracticeRequestPk(leagueId);

            var entity = await client.GetEntityAsync<TableEntity>(pk, requestId);
            if (entity is null)
                return ApiResponses.Error(req, HttpStatusCode.NotFound, "NOT_FOUND", "Request not found");

            entity.Value["Status"] = "Approved";
            entity.Value["ReviewedUtc"] = DateTimeOffset.UtcNow;
            entity.Value["ReviewedBy"] = me.UserId;
            if (!string.IsNullOrWhiteSpace(reason))
                entity.Value["ReviewReason"] = reason;

            await client.UpdateEntityAsync(entity.Value, ETag.All);

            // Send email notification
            try
            {
                var division = (entity.Value.GetString("Division") ?? "").Trim();
                var teamId = (entity.Value.GetString("TeamId") ?? "").Trim();
                var slotId = (entity.Value.GetString("SlotId") ?? "").Trim();

                // Fetch team for contact email
                var teamClient = await TableClients.GetTableAsync(_tableService, Constants.Tables.Teams);
                var teamEntity = await teamClient.GetEntityAsync<TableEntity>(Constants.Pk.Teams(leagueId, division), teamId);
                var teamName = (teamEntity.Value.GetString("Name") ?? teamId).Trim();
                var teamEmail = (teamEntity.Value.GetString("PrimaryContactEmail") ?? "").Trim();

                // Fetch slot for details
                var slot = await _slotRepo.GetSlotAsync(leagueId, division, slotId);

                if (!string.IsNullOrWhiteSpace(teamEmail) && slot != null)
                {
                    await _emailService.SendPracticeRequestApprovedEmailAsync(
                        to: teamEmail,
                        leagueId: leagueId,
                        teamName: teamName,
                        gameDate: (slot.GetString("GameDate") ?? "").Trim(),
                        startTime: (slot.GetString("StartTime") ?? "").Trim(),
                        endTime: (slot.GetString("EndTime") ?? "").Trim(),
                        field: (slot.GetString("DisplayName") ?? slot.GetString("FieldKey") ?? "TBD").Trim()
                    );
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to send practice request approved email for requestId {RequestId}", requestId);
                // Don't fail the request if email fails
            }

            return ApiResponses.Ok(req, ToDto(entity.Value));
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return ApiResponses.Error(req, HttpStatusCode.NotFound, "NOT_FOUND", "Request not found");
        }
        catch (ApiGuards.HttpError ex) { return ApiResponses.FromHttpError(req, ex); }
        catch (Exception ex)
        {
            _log.LogError(ex, "ApprovePracticeRequest failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
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

            // Authorization - admin only
            if (!await _membershipRepo.IsGlobalAdminAsync(me.UserId))
            {
                var membership = await _membershipRepo.GetMembershipAsync(me.UserId, leagueId);
                var role = (membership?.GetString("Role") ?? Constants.Roles.Viewer).Trim();
                if (!string.Equals(role, Constants.Roles.LeagueAdmin, StringComparison.OrdinalIgnoreCase))
                {
                    return ApiResponses.Error(req, HttpStatusCode.Forbidden, ErrorCodes.FORBIDDEN,
                        "Only league admins can reject practice requests");
                }
            }

            var body = await HttpUtil.ReadJsonAsync<ReviewPracticeRequestReq>(req);
            var reason = (body?.reason ?? "").Trim();

            var client = await TableClients.GetTableAsync(_tableService, Constants.Tables.PracticeRequests);
            var pk = PracticeRequestPk(leagueId);

            var entity = await client.GetEntityAsync<TableEntity>(pk, requestId);
            if (entity is null)
                return ApiResponses.Error(req, HttpStatusCode.NotFound, "NOT_FOUND", "Request not found");

            entity.Value["Status"] = "Rejected";
            entity.Value["ReviewedUtc"] = DateTimeOffset.UtcNow;
            entity.Value["ReviewedBy"] = me.UserId;
            if (!string.IsNullOrWhiteSpace(reason))
                entity.Value["ReviewReason"] = reason;

            await client.UpdateEntityAsync(entity.Value, ETag.All);

            // Send email notification
            try
            {
                var division = (entity.Value.GetString("Division") ?? "").Trim();
                var teamId = (entity.Value.GetString("TeamId") ?? "").Trim();
                var slotId = (entity.Value.GetString("SlotId") ?? "").Trim();

                // Fetch team for contact email
                var teamClient = await TableClients.GetTableAsync(_tableService, Constants.Tables.Teams);
                var teamEntity = await teamClient.GetEntityAsync<TableEntity>(Constants.Pk.Teams(leagueId, division), teamId);
                var teamName = (teamEntity.Value.GetString("Name") ?? teamId).Trim();
                var teamEmail = (teamEntity.Value.GetString("PrimaryContactEmail") ?? "").Trim();

                // Fetch slot for details
                var slot = await _slotRepo.GetSlotAsync(leagueId, division, slotId);

                if (!string.IsNullOrWhiteSpace(teamEmail) && slot != null)
                {
                    await _emailService.SendPracticeRequestRejectedEmailAsync(
                        to: teamEmail,
                        leagueId: leagueId,
                        teamName: teamName,
                        gameDate: (slot.GetString("GameDate") ?? "").Trim(),
                        startTime: (slot.GetString("StartTime") ?? "").Trim(),
                        endTime: (slot.GetString("EndTime") ?? "").Trim(),
                        reason: reason.Length > 0 ? reason : "Slot no longer available"
                    );
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to send practice request rejected email for requestId {RequestId}", requestId);
                // Don't fail the request if email fails
            }

            return ApiResponses.Ok(req, ToDto(entity.Value));
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return ApiResponses.Error(req, HttpStatusCode.NotFound, "NOT_FOUND", "Request not found");
        }
        catch (ApiGuards.HttpError ex) { return ApiResponses.FromHttpError(req, ex); }
        catch (Exception ex)
        {
            _log.LogError(ex, "RejectPracticeRequest failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
        }
    }
}
