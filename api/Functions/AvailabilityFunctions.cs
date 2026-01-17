using System.Net;
using GameSwap.Functions.Storage;
using GameSwap.Functions.Services;
using GameSwap.Functions.Repositories;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Extensions.OpenApi.Extensions;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.OpenApi.Models;
using Microsoft.Extensions.Logging;

namespace GameSwap.Functions.Functions;

/// <summary>
/// Azure Functions for field availability rules and exceptions.
/// Refactored to use service layer for business logic.
/// </summary>
public class AvailabilityFunctions
{
    private readonly IAvailabilityService _availabilityService;
    private readonly IMembershipRepository _membershipRepo;
    private readonly ILogger _log;

    public AvailabilityFunctions(IAvailabilityService availabilityService, IMembershipRepository membershipRepo, ILoggerFactory lf)
    {
        _availabilityService = availabilityService;
        _membershipRepo = membershipRepo;
        _log = lf.CreateLogger<AvailabilityFunctions>();
    }

    public record AvailabilityRuleRequest(
        string? ruleId,
        string? fieldKey,
        string? division,
        List<string>? divisionIds,
        string? startsOn,
        string? endsOn,
        List<string>? daysOfWeek,
        string? startTimeLocal,
        string? endTimeLocal,
        string? recurrencePattern,
        string? timezone,
        bool? isActive
    );

    public record AvailabilityRuleExceptionRequest(
        string? exceptionId,
        string? dateFrom,
        string? dateTo,
        string? startTimeLocal,
        string? endTimeLocal,
        string? reason
    );

    [Function("CreateAvailabilityRule")]
    [OpenApiOperation(operationId: "CreateAvailabilityRule", tags: new[] { "Availability Rules" }, Summary = "Create availability rule", Description = "Creates a new field availability rule that defines when a field is available for a division. Only league admins can create rules.")]
    [OpenApiSecurity("league_id_header", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "x-league-id")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(AvailabilityRuleRequest), Required = true, Description = "Availability rule details (fieldKey, division, schedule, timezone)")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Created, contentType: "application/json", bodyType: typeof(object), Description = "Rule created successfully")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.BadRequest, contentType: "application/json", bodyType: typeof(object), Description = "Invalid request (missing required fields or invalid dates/times)")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Forbidden, contentType: "application/json", bodyType: typeof(object), Description = "Unauthorized (not a league admin)")]
    public async Task<HttpResponseData> CreateRule(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "availability/rules")] HttpRequestData req)
    {
        try
        {
            // Extract context
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);

            // Authorization
            if (!await _membershipRepo.IsGlobalAdminAsync(me.UserId))
            {
                var membership = await _membershipRepo.GetMembershipAsync(me.UserId, leagueId);
                var role = (membership?.GetString("Role") ?? Constants.Roles.Viewer).Trim();
                if (!string.Equals(role, Constants.Roles.LeagueAdmin, StringComparison.OrdinalIgnoreCase))
                {
                    return ApiResponses.Error(req, HttpStatusCode.Forbidden, ErrorCodes.FORBIDDEN,
                        "Only league admins can create availability rules");
                }
            }

            // Parse body
            var body = await HttpUtil.ReadJsonAsync<AvailabilityRuleRequest>(req);
            if (body is null)
            {
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, ErrorCodes.BAD_REQUEST, "Invalid JSON body");
            }

            // Build service request
            var serviceRequest = new CreateAvailabilityRuleRequest
            {
                LeagueId = leagueId,
                RuleId = body.ruleId,
                FieldKey = (body.fieldKey ?? "").Trim(),
                Division = (body.division ?? "").Trim(),
                DivisionIds = body.divisionIds ?? new List<string>(),
                StartsOn = (body.startsOn ?? "").Trim(),
                EndsOn = (body.endsOn ?? "").Trim(),
                DaysOfWeek = body.daysOfWeek ?? new List<string>(),
                StartTimeLocal = (body.startTimeLocal ?? "").Trim(),
                EndTimeLocal = (body.endTimeLocal ?? "").Trim(),
                RecurrencePattern = (body.recurrencePattern ?? "Weekly").Trim(),
                Timezone = (body.timezone ?? "America/New_York").Trim(),
                IsActive = body.isActive ?? true
            };

            // Validate required fields
            if (string.IsNullOrWhiteSpace(serviceRequest.FieldKey))
            {
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, ErrorCodes.MISSING_REQUIRED_FIELD, "fieldKey is required");
            }
            if (string.IsNullOrWhiteSpace(serviceRequest.Division))
            {
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, ErrorCodes.MISSING_REQUIRED_FIELD, "division is required");
            }

            var context = CorrelationContext.FromRequest(req, leagueId);

            // Delegate to service
            var result = await _availabilityService.CreateRuleAsync(serviceRequest, context);

            return ApiResponses.Ok(req, result, HttpStatusCode.Created);
        }
        catch (ApiGuards.HttpError ex)
        {
            return ApiResponses.FromHttpError(req, ex);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "CreateAvailabilityRule failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, ErrorCodes.INTERNAL_ERROR, "An unexpected error occurred");
        }
    }

    [Function("GetAvailabilityRules")]
    [OpenApiOperation(operationId: "GetAvailabilityRules", tags: new[] { "Availability Rules" }, Summary = "Get availability rules", Description = "Retrieves all availability rules for a field. Only league admins can view rules.")]
    [OpenApiSecurity("league_id_header", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "x-league-id")]
    [OpenApiParameter(name: "fieldKey", In = ParameterLocation.Query, Required = true, Type = typeof(string), Description = "Field key in format 'ParkCode/FieldCode'")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object), Description = "Rules retrieved successfully")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.BadRequest, contentType: "application/json", bodyType: typeof(object), Description = "Invalid request (missing fieldKey)")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Forbidden, contentType: "application/json", bodyType: typeof(object), Description = "Unauthorized (not a league admin)")]
    public async Task<HttpResponseData> GetRules(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "availability/rules")] HttpRequestData req)
    {
        try
        {
            // Extract context
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);

            // Authorization
            if (!await _membershipRepo.IsGlobalAdminAsync(me.UserId))
            {
                var membership = await _membershipRepo.GetMembershipAsync(me.UserId, leagueId);
                var role = (membership?.GetString("Role") ?? Constants.Roles.Viewer).Trim();
                if (!string.Equals(role, Constants.Roles.LeagueAdmin, StringComparison.OrdinalIgnoreCase))
                {
                    return ApiResponses.Error(req, HttpStatusCode.Forbidden, ErrorCodes.FORBIDDEN,
                        "Only league admins can view availability rules");
                }
            }

            var fieldKey = (ApiGuards.GetQueryParam(req, "fieldKey") ?? "").Trim();
            if (string.IsNullOrWhiteSpace(fieldKey))
            {
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, ErrorCodes.MISSING_REQUIRED_FIELD, "fieldKey is required");
            }

            // Delegate to service
            var result = await _availabilityService.GetRulesAsync(leagueId, fieldKey);

            return ApiResponses.Ok(req, result);
        }
        catch (ApiGuards.HttpError ex)
        {
            return ApiResponses.FromHttpError(req, ex);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "GetAvailabilityRules failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, ErrorCodes.INTERNAL_ERROR, "An unexpected error occurred");
        }
    }

    [Function("UpdateAvailabilityRule")]
    [OpenApiOperation(operationId: "UpdateAvailabilityRule", tags: new[] { "Availability Rules" }, Summary = "Update availability rule", Description = "Updates an existing field availability rule. Only league admins can update rules.")]
    [OpenApiSecurity("league_id_header", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "x-league-id")]
    [OpenApiParameter(name: "ruleId", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Unique rule identifier")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(AvailabilityRuleRequest), Required = true, Description = "Updated rule details")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object), Description = "Rule updated successfully")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.BadRequest, contentType: "application/json", bodyType: typeof(object), Description = "Invalid request (missing required fields)")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Forbidden, contentType: "application/json", bodyType: typeof(object), Description = "Unauthorized (not a league admin)")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.NotFound, contentType: "application/json", bodyType: typeof(object), Description = "Rule not found")]
    public async Task<HttpResponseData> UpdateRule(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "availability/rules/{ruleId}")] HttpRequestData req,
        string ruleId)
    {
        try
        {
            // Extract context
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);

            // Authorization
            if (!await _membershipRepo.IsGlobalAdminAsync(me.UserId))
            {
                var membership = await _membershipRepo.GetMembershipAsync(me.UserId, leagueId);
                var role = (membership?.GetString("Role") ?? Constants.Roles.Viewer).Trim();
                if (!string.Equals(role, Constants.Roles.LeagueAdmin, StringComparison.OrdinalIgnoreCase))
                {
                    return ApiResponses.Error(req, HttpStatusCode.Forbidden, ErrorCodes.FORBIDDEN,
                        "Only league admins can update availability rules");
                }
            }

            // Parse body
            var body = await HttpUtil.ReadJsonAsync<AvailabilityRuleRequest>(req);
            if (body is null)
            {
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, ErrorCodes.BAD_REQUEST, "Invalid JSON body");
            }

            // Build service request
            var serviceRequest = new UpdateAvailabilityRuleRequest
            {
                LeagueId = leagueId,
                RuleId = ruleId.Trim(),
                FieldKey = (body.fieldKey ?? "").Trim(),
                Division = (body.division ?? "").Trim(),
                DivisionIds = body.divisionIds ?? new List<string>(),
                StartsOn = (body.startsOn ?? "").Trim(),
                EndsOn = (body.endsOn ?? "").Trim(),
                DaysOfWeek = body.daysOfWeek ?? new List<string>(),
                StartTimeLocal = (body.startTimeLocal ?? "").Trim(),
                EndTimeLocal = (body.endTimeLocal ?? "").Trim(),
                RecurrencePattern = (body.recurrencePattern ?? "Weekly").Trim(),
                Timezone = (body.timezone ?? "America/New_York").Trim(),
                IsActive = body.isActive ?? true
            };

            // Validate required fields
            if (string.IsNullOrWhiteSpace(serviceRequest.FieldKey))
            {
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, ErrorCodes.MISSING_REQUIRED_FIELD, "fieldKey is required");
            }
            if (string.IsNullOrWhiteSpace(serviceRequest.Division))
            {
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, ErrorCodes.MISSING_REQUIRED_FIELD, "division is required");
            }

            var context = CorrelationContext.FromRequest(req, leagueId);

            // Delegate to service
            var result = await _availabilityService.UpdateRuleAsync(serviceRequest, context);

            return ApiResponses.Ok(req, result);
        }
        catch (ApiGuards.HttpError ex)
        {
            return ApiResponses.FromHttpError(req, ex);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "UpdateAvailabilityRule failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, ErrorCodes.INTERNAL_ERROR, "An unexpected error occurred");
        }
    }

    [Function("DeactivateAvailabilityRule")]
    [OpenApiOperation(operationId: "DeactivateAvailabilityRule", tags: new[] { "Availability Rules" }, Summary = "Deactivate availability rule", Description = "Deactivates a field availability rule without deleting it. Only league admins can deactivate rules.")]
    [OpenApiSecurity("league_id_header", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "x-league-id")]
    [OpenApiParameter(name: "ruleId", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Unique rule identifier")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object), Description = "Rule deactivated successfully")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Forbidden, contentType: "application/json", bodyType: typeof(object), Description = "Unauthorized (not a league admin)")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.NotFound, contentType: "application/json", bodyType: typeof(object), Description = "Rule not found")]
    public async Task<HttpResponseData> DeactivateRule(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "availability/rules/{ruleId}/deactivate")] HttpRequestData req,
        string ruleId)
    {
        try
        {
            // Extract context
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);

            // Authorization
            if (!await _membershipRepo.IsGlobalAdminAsync(me.UserId))
            {
                var membership = await _membershipRepo.GetMembershipAsync(me.UserId, leagueId);
                var role = (membership?.GetString("Role") ?? Constants.Roles.Viewer).Trim();
                if (!string.Equals(role, Constants.Roles.LeagueAdmin, StringComparison.OrdinalIgnoreCase))
                {
                    return ApiResponses.Error(req, HttpStatusCode.Forbidden, ErrorCodes.FORBIDDEN,
                        "Only league admins can deactivate availability rules");
                }
            }

            // Delegate to service
            var result = await _availabilityService.DeactivateRuleAsync(leagueId, ruleId, me.UserId);

            return ApiResponses.Ok(req, result);
        }
        catch (ApiGuards.HttpError ex)
        {
            return ApiResponses.FromHttpError(req, ex);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "DeactivateAvailabilityRule failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, ErrorCodes.INTERNAL_ERROR, "An unexpected error occurred");
        }
    }

    [Function("CreateAvailabilityException")]
    [OpenApiOperation(operationId: "CreateAvailabilityException", tags: new[] { "Availability Exceptions" }, Summary = "Create availability exception", Description = "Creates an exception to an availability rule (e.g., field closure, special hours). Only league admins can create exceptions.")]
    [OpenApiSecurity("league_id_header", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "x-league-id")]
    [OpenApiParameter(name: "ruleId", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Rule identifier to add exception to")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(AvailabilityRuleExceptionRequest), Required = true, Description = "Exception details (dateFrom, dateTo, times, reason)")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Created, contentType: "application/json", bodyType: typeof(object), Description = "Exception created successfully")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.BadRequest, contentType: "application/json", bodyType: typeof(object), Description = "Invalid request (missing required fields)")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Forbidden, contentType: "application/json", bodyType: typeof(object), Description = "Unauthorized (not a league admin)")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.NotFound, contentType: "application/json", bodyType: typeof(object), Description = "Rule not found")]
    public async Task<HttpResponseData> CreateException(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "availability/rules/{ruleId}/exceptions")] HttpRequestData req,
        string ruleId)
    {
        try
        {
            // Extract context
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);

            // Authorization
            if (!await _membershipRepo.IsGlobalAdminAsync(me.UserId))
            {
                var membership = await _membershipRepo.GetMembershipAsync(me.UserId, leagueId);
                var role = (membership?.GetString("Role") ?? Constants.Roles.Viewer).Trim();
                if (!string.Equals(role, Constants.Roles.LeagueAdmin, StringComparison.OrdinalIgnoreCase))
                {
                    return ApiResponses.Error(req, HttpStatusCode.Forbidden, ErrorCodes.FORBIDDEN,
                        "Only league admins can create availability exceptions");
                }
            }

            // Parse body
            var body = await HttpUtil.ReadJsonAsync<AvailabilityRuleExceptionRequest>(req);
            if (body is null)
            {
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, ErrorCodes.BAD_REQUEST, "Invalid JSON body");
            }

            // Build service request
            var serviceRequest = new CreateAvailabilityExceptionRequest
            {
                LeagueId = leagueId,
                RuleId = ruleId.Trim(),
                ExceptionId = body.exceptionId,
                DateFrom = (body.dateFrom ?? "").Trim(),
                DateTo = (body.dateTo ?? "").Trim(),
                StartTimeLocal = (body.startTimeLocal ?? "").Trim(),
                EndTimeLocal = (body.endTimeLocal ?? "").Trim(),
                Reason = (body.reason ?? "").Trim()
            };

            var context = CorrelationContext.FromRequest(req, leagueId);

            // Delegate to service
            var result = await _availabilityService.CreateExceptionAsync(serviceRequest, context);

            return ApiResponses.Ok(req, result, HttpStatusCode.Created);
        }
        catch (ApiGuards.HttpError ex)
        {
            return ApiResponses.FromHttpError(req, ex);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "CreateAvailabilityException failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, ErrorCodes.INTERNAL_ERROR, "An unexpected error occurred");
        }
    }

    [Function("UpdateAvailabilityException")]
    [OpenApiOperation(operationId: "UpdateAvailabilityException", tags: new[] { "Availability Exceptions" }, Summary = "Update availability exception", Description = "Updates an existing availability exception. Only league admins can update exceptions.")]
    [OpenApiSecurity("league_id_header", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "x-league-id")]
    [OpenApiParameter(name: "ruleId", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Rule identifier")]
    [OpenApiParameter(name: "exceptionId", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Exception identifier")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(AvailabilityRuleExceptionRequest), Required = true, Description = "Updated exception details")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object), Description = "Exception updated successfully")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.BadRequest, contentType: "application/json", bodyType: typeof(object), Description = "Invalid request")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Forbidden, contentType: "application/json", bodyType: typeof(object), Description = "Unauthorized (not a league admin)")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.NotFound, contentType: "application/json", bodyType: typeof(object), Description = "Exception not found")]
    public async Task<HttpResponseData> UpdateException(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "availability/rules/{ruleId}/exceptions/{exceptionId}")] HttpRequestData req,
        string ruleId,
        string exceptionId)
    {
        try
        {
            // Extract context
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);

            // Authorization
            if (!await _membershipRepo.IsGlobalAdminAsync(me.UserId))
            {
                var membership = await _membershipRepo.GetMembershipAsync(me.UserId, leagueId);
                var role = (membership?.GetString("Role") ?? Constants.Roles.Viewer).Trim();
                if (!string.Equals(role, Constants.Roles.LeagueAdmin, StringComparison.OrdinalIgnoreCase))
                {
                    return ApiResponses.Error(req, HttpStatusCode.Forbidden, ErrorCodes.FORBIDDEN,
                        "Only league admins can update availability exceptions");
                }
            }

            // Parse body
            var body = await HttpUtil.ReadJsonAsync<AvailabilityRuleExceptionRequest>(req);
            if (body is null)
            {
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, ErrorCodes.BAD_REQUEST, "Invalid JSON body");
            }

            // Build service request
            var serviceRequest = new UpdateAvailabilityExceptionRequest
            {
                LeagueId = leagueId,
                RuleId = ruleId.Trim(),
                ExceptionId = exceptionId.Trim(),
                DateFrom = (body.dateFrom ?? "").Trim(),
                DateTo = (body.dateTo ?? "").Trim(),
                StartTimeLocal = (body.startTimeLocal ?? "").Trim(),
                EndTimeLocal = (body.endTimeLocal ?? "").Trim(),
                Reason = (body.reason ?? "").Trim()
            };

            var context = CorrelationContext.FromRequest(req, leagueId);

            // Delegate to service
            var result = await _availabilityService.UpdateExceptionAsync(serviceRequest, context);

            return ApiResponses.Ok(req, result);
        }
        catch (ApiGuards.HttpError ex)
        {
            return ApiResponses.FromHttpError(req, ex);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "UpdateAvailabilityException failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, ErrorCodes.INTERNAL_ERROR, "An unexpected error occurred");
        }
    }

    [Function("DeleteAvailabilityException")]
    [OpenApiOperation(operationId: "DeleteAvailabilityException", tags: new[] { "Availability Exceptions" }, Summary = "Delete availability exception", Description = "Deletes an availability exception. Only league admins can delete exceptions.")]
    [OpenApiSecurity("league_id_header", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "x-league-id")]
    [OpenApiParameter(name: "ruleId", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Rule identifier")]
    [OpenApiParameter(name: "exceptionId", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Exception identifier")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object), Description = "Exception deleted successfully")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Forbidden, contentType: "application/json", bodyType: typeof(object), Description = "Unauthorized (not a league admin)")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.NotFound, contentType: "application/json", bodyType: typeof(object), Description = "Exception not found")]
    public async Task<HttpResponseData> DeleteException(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "availability/rules/{ruleId}/exceptions/{exceptionId}")] HttpRequestData req,
        string ruleId,
        string exceptionId)
    {
        try
        {
            // Extract context
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);

            // Authorization
            if (!await _membershipRepo.IsGlobalAdminAsync(me.UserId))
            {
                var membership = await _membershipRepo.GetMembershipAsync(me.UserId, leagueId);
                var role = (membership?.GetString("Role") ?? Constants.Roles.Viewer).Trim();
                if (!string.Equals(role, Constants.Roles.LeagueAdmin, StringComparison.OrdinalIgnoreCase))
                {
                    return ApiResponses.Error(req, HttpStatusCode.Forbidden, ErrorCodes.FORBIDDEN,
                        "Only league admins can delete availability exceptions");
                }
            }

            // Delegate to service
            await _availabilityService.DeleteExceptionAsync(leagueId, ruleId, exceptionId, me.UserId);

            return ApiResponses.Ok(req, new { deleted = true });
        }
        catch (ApiGuards.HttpError ex)
        {
            return ApiResponses.FromHttpError(req, ex);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "DeleteAvailabilityException failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, ErrorCodes.INTERNAL_ERROR, "An unexpected error occurred");
        }
    }

    [Function("ListAvailabilityExceptions")]
    [OpenApiOperation(operationId: "ListAvailabilityExceptions", tags: new[] { "Availability Exceptions" }, Summary = "List availability exceptions", Description = "Retrieves all exceptions for a specific availability rule. Only league admins can list exceptions.")]
    [OpenApiSecurity("league_id_header", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "x-league-id")]
    [OpenApiParameter(name: "ruleId", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Rule identifier")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object), Description = "Exceptions retrieved successfully")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Forbidden, contentType: "application/json", bodyType: typeof(object), Description = "Unauthorized (not a league admin)")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.NotFound, contentType: "application/json", bodyType: typeof(object), Description = "Rule not found")]
    public async Task<HttpResponseData> ListExceptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "availability/rules/{ruleId}/exceptions")] HttpRequestData req,
        string ruleId)
    {
        try
        {
            // Extract context
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);

            // Authorization
            if (!await _membershipRepo.IsGlobalAdminAsync(me.UserId))
            {
                var membership = await _membershipRepo.GetMembershipAsync(me.UserId, leagueId);
                var role = (membership?.GetString("Role") ?? Constants.Roles.Viewer).Trim();
                if (!string.Equals(role, Constants.Roles.LeagueAdmin, StringComparison.OrdinalIgnoreCase))
                {
                    return ApiResponses.Error(req, HttpStatusCode.Forbidden, ErrorCodes.FORBIDDEN,
                        "Only league admins can list availability exceptions");
                }
            }

            // Delegate to service
            var result = await _availabilityService.ListExceptionsAsync(leagueId, ruleId);

            return ApiResponses.Ok(req, result);
        }
        catch (ApiGuards.HttpError ex)
        {
            return ApiResponses.FromHttpError(req, ex);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "ListAvailabilityExceptions failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, ErrorCodes.INTERNAL_ERROR, "An unexpected error occurred");
        }
    }

    [Function("PreviewAvailabilitySlots")]
    [OpenApiOperation(operationId: "PreviewAvailabilitySlots", tags: new[] { "Availability Rules" }, Summary = "Preview availability slots", Description = "Generates a preview of all available field slots for a date range based on availability rules and exceptions. Only league admins can preview slots.")]
    [OpenApiSecurity("league_id_header", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "x-league-id")]
    [OpenApiParameter(name: "dateFrom", In = ParameterLocation.Query, Required = true, Type = typeof(string), Description = "Start date for preview (YYYY-MM-DD)")]
    [OpenApiParameter(name: "dateTo", In = ParameterLocation.Query, Required = true, Type = typeof(string), Description = "End date for preview (YYYY-MM-DD)")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object), Description = "Preview generated successfully")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.BadRequest, contentType: "application/json", bodyType: typeof(object), Description = "Invalid date range")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Forbidden, contentType: "application/json", bodyType: typeof(object), Description = "Unauthorized (not a league admin)")]
    public async Task<HttpResponseData> PreviewAvailabilitySlots(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "availability/preview")] HttpRequestData req)
    {
        try
        {
            // Extract context
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);

            // Authorization
            if (!await _membershipRepo.IsGlobalAdminAsync(me.UserId))
            {
                var membership = await _membershipRepo.GetMembershipAsync(me.UserId, leagueId);
                var role = (membership?.GetString("Role") ?? Constants.Roles.Viewer).Trim();
                if (!string.Equals(role, Constants.Roles.LeagueAdmin, StringComparison.OrdinalIgnoreCase))
                {
                    return ApiResponses.Error(req, HttpStatusCode.Forbidden, ErrorCodes.FORBIDDEN,
                        "Only league admins can preview availability slots");
                }
            }

            var dateFromRaw = (ApiGuards.GetQueryParam(req, "dateFrom") ?? "").Trim();
            var dateToRaw = (ApiGuards.GetQueryParam(req, "dateTo") ?? "").Trim();
            if (!DateOnly.TryParseExact(dateFromRaw, "yyyy-MM-dd", out var dateFrom))
            {
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, ErrorCodes.INVALID_DATE, "dateFrom must be YYYY-MM-DD.");
            }
            if (!DateOnly.TryParseExact(dateToRaw, "yyyy-MM-dd", out var dateTo))
            {
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, ErrorCodes.INVALID_DATE, "dateTo must be YYYY-MM-DD.");
            }
            if (dateTo < dateFrom)
            {
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, ErrorCodes.INVALID_DATE_RANGE, "dateTo must be on or after dateFrom.");
            }

            // Delegate to service
            var result = await _availabilityService.PreviewSlotsAsync(leagueId, dateFrom, dateTo);

            return ApiResponses.Ok(req, new { slots = result });
        }
        catch (ApiGuards.HttpError ex)
        {
            return ApiResponses.FromHttpError(req, ex);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "PreviewAvailabilitySlots failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, ErrorCodes.INTERNAL_ERROR, "An unexpected error occurred");
        }
    }
}
