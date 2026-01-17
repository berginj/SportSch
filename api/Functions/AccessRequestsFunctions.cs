using System.Net;
using Azure;
using Azure.Data.Tables;
using GameSwap.Functions.Repositories;
using GameSwap.Functions.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Extensions.OpenApi.Extensions;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.OpenApi.Models;
using Microsoft.Extensions.Logging;

namespace GameSwap.Functions.Functions;

public class AccessRequestsFunctions
{
    private readonly IAccessRequestRepository _accessRequestRepo;
    private readonly IMembershipRepository _membershipRepo;
    private readonly TableServiceClient _tableService; // Still needed for league existence check
    private readonly ILogger _log;

    public AccessRequestsFunctions(
        IAccessRequestRepository accessRequestRepo,
        IMembershipRepository membershipRepo,
        TableServiceClient tableService,
        ILoggerFactory lf)
    {
        _accessRequestRepo = accessRequestRepo;
        _membershipRepo = membershipRepo;
        _tableService = tableService;
        _log = lf.CreateLogger<AccessRequestsFunctions>();
    }

    public record CreateAccessRequestReq(string? requestedRole, string? notes);
    public record ApproveAccessReq(string? role, CoachTeam? team);
    public record DenyAccessReq(string? reason);
    public record CoachTeam(string? division, string? teamId);

    public record AccessRequestDto(
        string leagueId,
        string userId,
        string email,
        string requestedRole,
        string status,
        string notes,
        DateTimeOffset createdUtc,
        DateTimeOffset updatedUtc);

    private static string ReqPk(string leagueId) => Constants.Pk.AccessRequests(leagueId);
    private static string ReqRk(string userId) => userId; // one request per (league,user)

    private const string AccessReqPkPrefix = "ACCESSREQ|";
    private const string NewLeagueId = "NEW_LEAGUE";

    private static string LeagueIdFromPk(string pk)
    {
        if (string.IsNullOrWhiteSpace(pk)) return "";
        if (!pk.StartsWith(AccessReqPkPrefix, StringComparison.OrdinalIgnoreCase)) return "";
        return pk[AccessReqPkPrefix.Length..];
    }

    private static AccessRequestDto ToDto(TableEntity e)
        => new(
            leagueId: (e.GetString("LeagueId") ?? "").Trim(),
            userId: (e.GetString("UserId") ?? e.RowKey).Trim(),
            email: (e.GetString("Email") ?? "").Trim(),
            requestedRole: (e.GetString("RequestedRole") ?? "").Trim(),
            status: (e.GetString("Status") ?? Constants.Status.AccessRequestPending).Trim(),
            notes: (e.GetString("Notes") ?? "").Trim(),
            createdUtc: e.GetDateTimeOffset("CreatedUtc") ?? DateTimeOffset.MinValue,
            updatedUtc: e.GetDateTimeOffset("UpdatedUtc") ?? DateTimeOffset.MinValue
        );

    private static AccessRequestDto ToDtoWithFallback(TableEntity e)
    {
        var dto = ToDto(e);
        if (!string.IsNullOrWhiteSpace(dto.leagueId)) return dto;
        return dto with { leagueId = LeagueIdFromPk(e.PartitionKey) };
    }

    private static bool IsValidRequestedRole(string role)
        => string.Equals(role, Constants.Roles.Coach, StringComparison.OrdinalIgnoreCase)
           || string.Equals(role, Constants.Roles.Viewer, StringComparison.OrdinalIgnoreCase)
           || string.Equals(role, Constants.Roles.LeagueAdmin, StringComparison.OrdinalIgnoreCase);

    private static bool IsTruthy(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        return value.Equals("1", StringComparison.OrdinalIgnoreCase)
               || value.Equals("true", StringComparison.OrdinalIgnoreCase)
               || value.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    [Function("CreateAccessRequest")]
    [OpenApiOperation(operationId: "CreateAccessRequest", tags: new[] { "Access Requests" }, Summary = "Create access request", Description = "Creates a new access request for a league. Users request membership with a specific role (Viewer, Coach, or LeagueAdmin).")]
    [OpenApiSecurity("league_id_header", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "x-league-id")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(CreateAccessRequestReq), Required = false, Description = "Access request details (requestedRole, notes)")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Created, contentType: "application/json", bodyType: typeof(object), Description = "Access request created successfully")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.BadRequest, contentType: "application/json", bodyType: typeof(object), Description = "Invalid request (invalid role or league not found)")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Conflict, contentType: "application/json", bodyType: typeof(object), Description = "Request already exists or user already has membership")]
    public async Task<HttpResponseData> Create(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "accessrequests")] HttpRequestData req)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            if (string.IsNullOrWhiteSpace(me.UserId) || me.UserId == "UNKNOWN"
                || string.IsNullOrWhiteSpace(me.Email) || me.Email == "UNKNOWN")
            {
                return ApiResponses.Error(req, HttpStatusCode.Unauthorized,
                    "UNAUTHENTICATED", "You must be signed in.");
            }
            ApiGuards.EnsureValidTableKeyPart("userId", me.UserId);

            var body = await HttpUtil.ReadJsonAsync<CreateAccessRequestReq>(req);
            if (body is null)
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "Invalid JSON body");

            var requestedRole = (body.requestedRole ?? "").Trim();
            var notes = (body.notes ?? "").Trim();

            if (string.IsNullOrWhiteSpace(requestedRole))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "requestedRole is required");

            if (!IsValidRequestedRole(requestedRole))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "requestedRole must be Coach, Viewer, or LeagueAdmin");

            if (!string.Equals(leagueId, NewLeagueId, StringComparison.OrdinalIgnoreCase))
            {
                // Ensure league exists
                var leagues = await TableClients.GetTableAsync(_tableService, Constants.Tables.Leagues);
                try
                {
                    _ = (await leagues.GetEntityAsync<TableEntity>(Constants.Pk.Leagues, leagueId)).Value;
                }
                catch (RequestFailedException ex) when (ex.Status == 404)
                {
                    return ApiResponses.Error(req, HttpStatusCode.NotFound, "NOT_FOUND", $"league not found: {leagueId}");
                }
            }

            var pk = ReqPk(leagueId);
            var rk = ReqRk(me.UserId);
            var now = DateTimeOffset.UtcNow;

            // Preserve CreatedUtc if existing
            DateTimeOffset createdUtc = now;
            var existing = await _accessRequestRepo.GetAccessRequestAsync(leagueId, me.UserId);
            if (existing is not null)
            {
                createdUtc = existing.GetDateTimeOffset("CreatedUtc") ?? now;

                var existingStatus = (existing.GetString("Status") ?? Constants.Status.AccessRequestPending).Trim();
                if (string.Equals(existingStatus, Constants.Status.AccessRequestApproved, StringComparison.OrdinalIgnoreCase))
                    return ApiResponses.Error(req, HttpStatusCode.Conflict, "CONFLICT", "Access already approved for this league.");
            }

            var entity = new TableEntity(pk, rk)
            {
                ["LeagueId"] = leagueId,
                ["UserId"] = me.UserId,
                ["Email"] = me.Email,
                ["RequestedRole"] = requestedRole,
                ["Status"] = Constants.Status.AccessRequestPending,
                ["Notes"] = notes,
                ["CreatedUtc"] = createdUtc,
                ["UpdatedUtc"] = now
            };

            await _accessRequestRepo.UpsertAccessRequestAsync(entity);
            return ApiResponses.Ok(req, ToDto(entity));
        }
        catch (ApiGuards.HttpError ex)
        {
            return ApiResponses.FromHttpError(req, ex);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "CreateAccessRequest failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
        }
    }

    [Function("ListMyAccessRequests")]
    [OpenApiOperation(operationId: "ListMyAccessRequests", tags: new[] { "Access Requests" }, Summary = "List my access requests", Description = "Retrieves all access requests for the current user across all leagues.")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object), Description = "Access requests retrieved successfully")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Unauthorized, contentType: "application/json", bodyType: typeof(object), Description = "User not signed in")]
    public async Task<HttpResponseData> ListMine(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "accessrequests/mine")] HttpRequestData req)
    {
        try
        {
            var me = IdentityUtil.GetMe(req);
            if (string.IsNullOrWhiteSpace(me.UserId) || me.UserId == "UNKNOWN")
            {
                return ApiResponses.Error(req, HttpStatusCode.Unauthorized,
                    "UNAUTHENTICATED", "You must be signed in.");
            }

            var entities = await _accessRequestRepo.QueryAccessRequestsByUserIdAsync(me.UserId);
            var list = new List<AccessRequestDto>();
            foreach (var e in entities)
                list.Add(ToDtoWithFallback(e));

            return ApiResponses.Ok(req, list.OrderByDescending(x => x.updatedUtc));
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "ListMyAccessRequests failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
        }
    }

    [Function("ListAccessRequests")]
    [OpenApiOperation(operationId: "ListAccessRequests", tags: new[] { "Access Requests" }, Summary = "List access requests", Description = "Retrieves access requests for a league (league admins) or all leagues (global admins). Filter by status (Pending, Approved, Denied).")]
    [OpenApiSecurity("league_id_header", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "x-league-id")]
    [OpenApiParameter(name: "status", In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = "Filter by status (Pending, Approved, Denied). Default: Pending")]
    [OpenApiParameter(name: "all", In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = "Set to 'true' to get requests across all leagues (global admins only)")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object), Description = "Access requests retrieved successfully")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Unauthorized, contentType: "application/json", bodyType: typeof(object), Description = "User not signed in")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Forbidden, contentType: "application/json", bodyType: typeof(object), Description = "Not authorized (not a league admin)")]
    public async Task<HttpResponseData> ListForLeague(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "accessrequests")] HttpRequestData req)
    {
        try
        {
            var me = IdentityUtil.GetMe(req);
            if (string.IsNullOrWhiteSpace(me.UserId) || me.UserId == "UNKNOWN")
            {
                return ApiResponses.Error(req, HttpStatusCode.Unauthorized,
                    "UNAUTHENTICATED", "You must be signed in.");
            }
            var status = (ApiGuards.GetQueryParam(req, "status") ?? Constants.Status.AccessRequestPending).Trim();
            var all = IsTruthy(ApiGuards.GetQueryParam(req, "all"));

            var list = new List<AccessRequestDto>();

            if (all)
            {
                // Authorization - global admin only
                if (!await _membershipRepo.IsGlobalAdminAsync(me.UserId))
                {
                    return ApiResponses.Error(req, HttpStatusCode.Forbidden, ErrorCodes.FORBIDDEN,
                        "Only global admins can list all access requests");
                }

                var entities = await _accessRequestRepo.QueryAllAccessRequestsAsync(status);
                foreach (var e in entities)
                    list.Add(ToDtoWithFallback(e));

                return ApiResponses.Ok(req, list.OrderBy(x => x.leagueId).ThenBy(x => x.email));
            }

            var leagueId = ApiGuards.RequireLeagueId(req);

            // Authorization - league admin required
            if (!await _membershipRepo.IsGlobalAdminAsync(me.UserId))
            {
                var membership = await _membershipRepo.GetMembershipAsync(me.UserId, leagueId);
                var role = (membership?.GetString("Role") ?? Constants.Roles.Viewer).Trim();
                if (!string.Equals(role, Constants.Roles.LeagueAdmin, StringComparison.OrdinalIgnoreCase))
                {
                    return ApiResponses.Error(req, HttpStatusCode.Forbidden, ErrorCodes.FORBIDDEN,
                        "Only league admins can list access requests");
                }
            }

            var entities2 = await _accessRequestRepo.QueryAccessRequestsByLeagueAsync(leagueId, status);
            foreach (var e in entities2)
                list.Add(ToDtoWithFallback(e));

            return ApiResponses.Ok(req, list.OrderBy(x => x.email));
        }
        catch (ApiGuards.HttpError ex)
        {
            return ApiResponses.FromHttpError(req, ex);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "ListAccessRequests failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
        }
    }

    [Function("ApproveAccessRequest")]
    [OpenApiOperation(operationId: "ApproveAccessRequest", tags: new[] { "Access Requests" }, Summary = "Approve access request", Description = "Approves a pending access request and creates a membership for the user. Only league admins can approve requests.")]
    [OpenApiSecurity("league_id_header", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "x-league-id")]
    [OpenApiParameter(name: "userId", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "User ID of the requester")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(ApproveAccessReq), Required = false, Description = "Approval details (role override, team assignment for coaches)")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object), Description = "Access request approved successfully")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.BadRequest, contentType: "application/json", bodyType: typeof(object), Description = "Invalid request (invalid role)")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Unauthorized, contentType: "application/json", bodyType: typeof(object), Description = "User not signed in")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Forbidden, contentType: "application/json", bodyType: typeof(object), Description = "Not authorized (not a league admin)")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.NotFound, contentType: "application/json", bodyType: typeof(object), Description = "Access request not found")]
    public async Task<HttpResponseData> Approve(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "accessrequests/{userId}/approve")] HttpRequestData req,
        string userId)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            if (string.IsNullOrWhiteSpace(me.UserId) || me.UserId == "UNKNOWN")
            {
                return ApiResponses.Error(req, HttpStatusCode.Unauthorized,
                    "UNAUTHENTICATED", "You must be signed in.");
            }

            // Authorization - league admin required
            if (!await _membershipRepo.IsGlobalAdminAsync(me.UserId))
            {
                var membership = await _membershipRepo.GetMembershipAsync(me.UserId, leagueId);
                var myRole = (membership?.GetString("Role") ?? Constants.Roles.Viewer).Trim();
                if (!string.Equals(myRole, Constants.Roles.LeagueAdmin, StringComparison.OrdinalIgnoreCase))
                {
                    return ApiResponses.Error(req, HttpStatusCode.Forbidden, ErrorCodes.FORBIDDEN,
                        "Only league admins can approve access requests");
                }
            }

            userId = (userId ?? "").Trim();
            ApiGuards.EnsureValidTableKeyPart("userId", userId);

            var ar = await _accessRequestRepo.GetAccessRequestAsync(leagueId, userId);
            if (ar is null)
            {
                return ApiResponses.Error(req, HttpStatusCode.NotFound, "NOT_FOUND", "access request not found");
            }

            var body = await HttpUtil.ReadJsonAsync<ApproveAccessReq>(req) ?? new ApproveAccessReq(null, null);

            // Default: honor requestedRole
            var requestedRole = (ar.GetString("RequestedRole") ?? Constants.Roles.Viewer).Trim();
            var role = (body.role ?? requestedRole).Trim();

            if (!IsValidRequestedRole(role))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "role must be Coach, Viewer, or LeagueAdmin");

            var division = (body.team?.division ?? "").Trim();
            var teamId = (body.team?.teamId ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(division))
                ApiGuards.EnsureValidTableKeyPart("division", division);
            if (!string.IsNullOrWhiteSpace(teamId))
                ApiGuards.EnsureValidTableKeyPart("teamId", teamId);

            // Upsert membership (PK=userId, RK=leagueId)
            var mem = new TableEntity(userId, leagueId)
            {
                ["Role"] = role,
                ["Email"] = (ar.GetString("Email") ?? "").Trim(),
                ["Division"] = division,
                ["TeamId"] = teamId,
                ["UpdatedUtc"] = DateTimeOffset.UtcNow
            };

            await _membershipRepo.UpsertMembershipAsync(mem);

            // Mark request approved
            ar["Status"] = Constants.Status.AccessRequestApproved;
            ar["UpdatedUtc"] = DateTimeOffset.UtcNow;
            await _accessRequestRepo.UpdateAccessRequestAsync(ar);

            return ApiResponses.Ok(req, new { leagueId, userId, status = Constants.Status.AccessRequestApproved });
        }
        catch (ApiGuards.HttpError ex)
        {
            return ApiResponses.FromHttpError(req, ex);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "ApproveAccessRequest failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
        }
    }

    [Function("DenyAccessRequest")]
    [OpenApiOperation(operationId: "DenyAccessRequest", tags: new[] { "Access Requests" }, Summary = "Deny access request", Description = "Denies a pending access request. Only league admins can deny requests.")]
    [OpenApiSecurity("league_id_header", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "x-league-id")]
    [OpenApiParameter(name: "userId", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "User ID of the requester")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(DenyAccessReq), Required = false, Description = "Denial details (reason)")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object), Description = "Access request denied successfully")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Unauthorized, contentType: "application/json", bodyType: typeof(object), Description = "User not signed in")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Forbidden, contentType: "application/json", bodyType: typeof(object), Description = "Not authorized (not a league admin)")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.NotFound, contentType: "application/json", bodyType: typeof(object), Description = "Access request not found")]
    public async Task<HttpResponseData> Deny(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "accessrequests/{userId}/deny")] HttpRequestData req,
        string userId)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            if (string.IsNullOrWhiteSpace(me.UserId) || me.UserId == "UNKNOWN")
            {
                return ApiResponses.Error(req, HttpStatusCode.Unauthorized,
                    "UNAUTHENTICATED", "You must be signed in.");
            }

            // Authorization - league admin required
            if (!await _membershipRepo.IsGlobalAdminAsync(me.UserId))
            {
                var membership = await _membershipRepo.GetMembershipAsync(me.UserId, leagueId);
                var myRole = (membership?.GetString("Role") ?? Constants.Roles.Viewer).Trim();
                if (!string.Equals(myRole, Constants.Roles.LeagueAdmin, StringComparison.OrdinalIgnoreCase))
                {
                    return ApiResponses.Error(req, HttpStatusCode.Forbidden, ErrorCodes.FORBIDDEN,
                        "Only league admins can deny access requests");
                }
            }

            userId = (userId ?? "").Trim();
            ApiGuards.EnsureValidTableKeyPart("userId", userId);

            var ar = await _accessRequestRepo.GetAccessRequestAsync(leagueId, userId);
            if (ar is null)
            {
                return ApiResponses.Error(req, HttpStatusCode.NotFound, "NOT_FOUND", "access request not found");
            }

            var body = await HttpUtil.ReadJsonAsync<DenyAccessReq>(req);
            var reason = (body?.reason ?? "").Trim();

            ar["Status"] = Constants.Status.AccessRequestDenied;
            ar["DeniedReason"] = reason;
            ar["UpdatedUtc"] = DateTimeOffset.UtcNow;
            await _accessRequestRepo.UpdateAccessRequestAsync(ar);

            return ApiResponses.Ok(req, new { leagueId, userId, status = Constants.Status.AccessRequestDenied });
        }
        catch (ApiGuards.HttpError ex)
        {
            return ApiResponses.FromHttpError(req, ex);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "DenyAccessRequest failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
        }
    }
}
