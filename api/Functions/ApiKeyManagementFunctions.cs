using System.Net;
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
/// Admin functions for managing API key rotation.
/// Only global admins can access these endpoints.
/// </summary>
public class ApiKeyManagementFunctions
{
    private readonly IApiKeyService _apiKeyService;
    private readonly IMembershipRepository _membershipRepo;
    private readonly IAuditLogger _auditLogger;
    private readonly ILogger _log;

    public ApiKeyManagementFunctions(
        IApiKeyService apiKeyService,
        IMembershipRepository membershipRepo,
        IAuditLogger auditLogger,
        ILoggerFactory lf)
    {
        _apiKeyService = apiKeyService;
        _membershipRepo = membershipRepo;
        _auditLogger = auditLogger;
        _log = lf.CreateLogger<ApiKeyManagementFunctions>();
    }

    [Function("GetApiKeys")]
    [OpenApiOperation(operationId: "GetApiKeys", tags: new[] { "API Keys" },
        Summary = "Get active API keys",
        Description = "Retrieves the current primary and secondary API keys. Only global admins can access this endpoint.")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json",
        bodyType: typeof(object), Description = "API key information retrieved successfully")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Forbidden, contentType: "application/json",
        bodyType: typeof(object), Description = "Only global admins can manage API keys")]
    public async Task<HttpResponseData> GetKeys(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "admin/api-keys")] HttpRequestData req)
    {
        try
        {
            var me = IdentityUtil.GetMe(req);

            // Authorization - global admin only
            if (!await _membershipRepo.IsGlobalAdminAsync(me.UserId))
            {
                return ApiResponses.Error(req, HttpStatusCode.Forbidden, ErrorCodes.FORBIDDEN,
                    "Only global admins can manage API keys");
            }

            var keys = await _apiKeyService.GetActiveKeysAsync();

            _auditLogger.LogApiKeyAccess(me.UserId, "VIEW");

            return ApiResponses.Ok(req, new
            {
                primaryKey = keys.PrimaryKey,
                secondaryKey = keys.SecondaryKey,
                primaryKeyCreatedUtc = keys.PrimaryKeyCreatedUtc,
                secondaryKeyCreatedUtc = keys.SecondaryKeyCreatedUtc,
                lastRotatedBy = keys.LastRotatedBy,
                lastRotatedUtc = keys.LastRotatedUtc
            });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "GetApiKeys failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
        }
    }

    [Function("RotateApiKeys")]
    [OpenApiOperation(operationId: "RotateApiKeys", tags: new[] { "API Keys" },
        Summary = "Rotate API keys",
        Description = "Rotates API keys using zero-downtime strategy: secondary becomes primary, new secondary is generated. Only global admins can rotate keys.")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json",
        bodyType: typeof(object), Description = "API keys rotated successfully")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Forbidden, contentType: "application/json",
        bodyType: typeof(object), Description = "Only global admins can manage API keys")]
    public async Task<HttpResponseData> RotateKeys(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "admin/api-keys/rotate")] HttpRequestData req)
    {
        try
        {
            var me = IdentityUtil.GetMe(req);

            // Authorization - global admin only
            if (!await _membershipRepo.IsGlobalAdminAsync(me.UserId))
            {
                return ApiResponses.Error(req, HttpStatusCode.Forbidden, ErrorCodes.FORBIDDEN,
                    "Only global admins can manage API keys");
            }

            var keys = await _apiKeyService.RotateKeysAsync(me.UserId);

            _auditLogger.LogApiKeyRotation(me.UserId, "ROTATE",
                keys.PrimaryKeyCreatedUtc, keys.SecondaryKeyCreatedUtc);

            return ApiResponses.Ok(req, new
            {
                primaryKey = keys.PrimaryKey,
                secondaryKey = keys.SecondaryKey,
                primaryKeyCreatedUtc = keys.PrimaryKeyCreatedUtc,
                secondaryKeyCreatedUtc = keys.SecondaryKeyCreatedUtc,
                lastRotatedBy = keys.LastRotatedBy,
                lastRotatedUtc = keys.LastRotatedUtc,
                message = "API keys rotated successfully. Update your clients with the new keys."
            });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "RotateApiKeys failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
        }
    }

    [Function("RegenerateSecondaryKey")]
    [OpenApiOperation(operationId: "RegenerateSecondaryKey", tags: new[] { "API Keys" },
        Summary = "Regenerate secondary key",
        Description = "Regenerates only the secondary API key without rotating. Useful for emergency revocation. Only global admins can regenerate keys.")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json",
        bodyType: typeof(object), Description = "Secondary key regenerated successfully")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Forbidden, contentType: "application/json",
        bodyType: typeof(object), Description = "Only global admins can manage API keys")]
    public async Task<HttpResponseData> RegenerateSecondary(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "admin/api-keys/regenerate-secondary")] HttpRequestData req)
    {
        try
        {
            var me = IdentityUtil.GetMe(req);

            // Authorization - global admin only
            if (!await _membershipRepo.IsGlobalAdminAsync(me.UserId))
            {
                return ApiResponses.Error(req, HttpStatusCode.Forbidden, ErrorCodes.FORBIDDEN,
                    "Only global admins can manage API keys");
            }

            var keys = await _apiKeyService.RegenerateSecondaryKeyAsync(me.UserId);

            _auditLogger.LogApiKeyRotation(me.UserId, "REGENERATE_SECONDARY",
                keys.PrimaryKeyCreatedUtc, keys.SecondaryKeyCreatedUtc);

            return ApiResponses.Ok(req, new
            {
                primaryKey = keys.PrimaryKey,
                secondaryKey = keys.SecondaryKey,
                primaryKeyCreatedUtc = keys.PrimaryKeyCreatedUtc,
                secondaryKeyCreatedUtc = keys.SecondaryKeyCreatedUtc,
                lastRotatedBy = keys.LastRotatedBy,
                lastRotatedUtc = keys.LastRotatedUtc,
                message = "Secondary API key regenerated successfully."
            });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "RegenerateSecondaryKey failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
        }
    }

    [Function("GetApiKeyHistory")]
    [OpenApiOperation(operationId: "GetApiKeyHistory", tags: new[] { "API Keys" },
        Summary = "Get API key rotation history",
        Description = "Retrieves the history of API key rotations for audit purposes. Only global admins can access history.")]
    [OpenApiParameter(name: "limit", In = ParameterLocation.Query, Required = false,
        Type = typeof(int), Description = "Maximum number of history entries (default: 10)")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json",
        bodyType: typeof(object), Description = "API key rotation history retrieved successfully")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Forbidden, contentType: "application/json",
        bodyType: typeof(object), Description = "Only global admins can manage API keys")]
    public async Task<HttpResponseData> GetHistory(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "admin/api-keys/history")] HttpRequestData req)
    {
        try
        {
            var me = IdentityUtil.GetMe(req);

            // Authorization - global admin only
            if (!await _membershipRepo.IsGlobalAdminAsync(me.UserId))
            {
                return ApiResponses.Error(req, HttpStatusCode.Forbidden, ErrorCodes.FORBIDDEN,
                    "Only global admins can manage API keys");
            }

            var limitStr = ApiGuards.GetQueryParam(req, "limit");
            var limit = int.TryParse(limitStr, out var l) ? l : 10;

            var history = await _apiKeyService.GetRotationHistoryAsync(limit);

            _auditLogger.LogApiKeyAccess(me.UserId, "VIEW_HISTORY");

            return ApiResponses.Ok(req, history);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "GetApiKeyHistory failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
        }
    }
}
