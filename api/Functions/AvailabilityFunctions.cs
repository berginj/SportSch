using System.Net;
using GameSwap.Functions.Storage;
using GameSwap.Functions.Services;
using GameSwap.Functions.Repositories;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
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
