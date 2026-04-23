using System.Net;
using GameSwap.Functions.Repositories;
using GameSwap.Functions.Services;
using GameSwap.Functions.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace GameSwap.Functions.Functions;

/// <summary>
/// Azure Functions for umpire self-service operations.
/// These endpoints allow umpires to view and manage their own assignments.
/// </summary>
public class UmpireSelfServiceFunctions
{
    private readonly IUmpireAssignmentService _assignmentService;
    private readonly IUmpireProfileRepository _umpireRepo;
    private readonly ILogger _log;

    public UmpireSelfServiceFunctions(
        IUmpireAssignmentService assignmentService,
        IUmpireProfileRepository umpireRepo,
        ILoggerFactory loggerFactory)
    {
        _assignmentService = assignmentService;
        _umpireRepo = umpireRepo;
        _log = loggerFactory.CreateLogger<UmpireSelfServiceFunctions>();
    }

    [Function("GetMyUmpireAssignments")]
    public async Task<HttpResponseData> GetMyAssignments(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "umpires/me/assignments")] HttpRequestData req)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);

            // Verify user is an umpire in this league
            var umpire = await _umpireRepo.GetUmpireAsync(leagueId, me.UserId);
            if (umpire == null)
            {
                return ApiResponses.Error(req, HttpStatusCode.Forbidden, ErrorCodes.FORBIDDEN,
                    "You are not registered as an umpire in this league");
            }

            // Query parameters
            var status = ApiGuards.GetQueryParam(req, "status");
            var dateFrom = ApiGuards.GetQueryParam(req, "dateFrom");
            var dateTo = ApiGuards.GetQueryParam(req, "dateTo");

            var filter = new AssignmentQueryFilter
            {
                LeagueId = leagueId,
                UmpireUserId = me.UserId,
                Status = status,
                DateFrom = dateFrom,
                DateTo = dateTo
            };

            var assignments = await _assignmentService.GetUmpireAssignmentsAsync(me.UserId, filter);

            return ApiResponses.Ok(req, assignments);
        }
        catch (ApiGuards.HttpError ex) { return ApiResponses.FromHttpError(req, ex); }
        catch (Exception ex)
        {
            _log.LogError(ex, "GetMyAssignments failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, ErrorCodes.INTERNAL_ERROR, "Internal Server Error");
        }
    }

    [Function("GetMyUmpireDashboard")]
    public async Task<HttpResponseData> GetMyDashboard(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "umpires/me/dashboard")] HttpRequestData req)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);

            // Verify user is an umpire
            var umpire = await _umpireRepo.GetUmpireAsync(leagueId, me.UserId);
            if (umpire == null)
            {
                return ApiResponses.Error(req, HttpStatusCode.Forbidden, ErrorCodes.FORBIDDEN,
                    "You are not registered as an umpire in this league");
            }

            // Get all assignments for summary counts
            var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
            var allAssignments = await _assignmentService.GetUmpireAssignmentsAsync(me.UserId, new AssignmentQueryFilter
            {
                LeagueId = leagueId,
                DateFrom = today
            });

            var pending = allAssignments.Count(a =>
            {
                var status = GetPropertyString(a, "status");
                return status == "Assigned";
            });

            var upcoming = allAssignments.Count(a =>
            {
                var status = GetPropertyString(a, "status");
                return status == "Accepted";
            });

            var thisWeek = allAssignments.Count(a => IsThisWeek(GetPropertyString(a, "gameDate")));
            var thisMonth = allAssignments.Count(a => IsThisMonth(GetPropertyString(a, "gameDate")));

            var dashboard = new
            {
                umpire = new
                {
                    umpireUserId = umpire.RowKey,
                    name = umpire.GetString("Name"),
                    email = umpire.GetString("Email"),
                    phone = umpire.GetString("Phone"),
                    certificationLevel = umpire.GetString("CertificationLevel"),
                    yearsExperience = umpire.GetInt32("YearsExperience")
                },
                pendingCount = pending,
                upcomingCount = upcoming,
                thisWeek,
                thisMonth
            };

            return ApiResponses.Ok(req, dashboard);
        }
        catch (ApiGuards.HttpError ex) { return ApiResponses.FromHttpError(req, ex); }
        catch (Exception ex)
        {
            _log.LogError(ex, "GetMyDashboard failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, ErrorCodes.INTERNAL_ERROR, "Internal Server Error");
        }
    }

    // Helper methods
    private static string GetPropertyString(object obj, string propertyName)
    {
        var prop = obj.GetType().GetProperty(propertyName);
        return prop?.GetValue(obj)?.ToString() ?? "";
    }

    private static bool IsThisWeek(string dateStr)
    {
        if (string.IsNullOrWhiteSpace(dateStr)) return false;

        if (!DateTime.TryParse(dateStr, out var date)) return false;

        var now = DateTime.UtcNow;
        var startOfWeek = now.Date.AddDays(-(int)now.DayOfWeek);
        var endOfWeek = startOfWeek.AddDays(7);

        return date >= startOfWeek && date < endOfWeek;
    }

    private static bool IsThisMonth(string dateStr)
    {
        if (string.IsNullOrWhiteSpace(dateStr)) return false;

        if (!DateTime.TryParse(dateStr, out var date)) return false;

        var now = DateTime.UtcNow;
        return date.Year == now.Year && date.Month == now.Month;
    }
}
