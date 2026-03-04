using System.Net;
using System.Text.Json;
using Azure.Data.Tables;
using GameSwap.Functions.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace GameSwap.Functions.Functions;

/// <summary>
/// Captures user feedback on schedule generation for ML improvement.
/// Tracks which schedules users select and why, enabling data-driven optimization.
/// </summary>
public class ScheduleFeedbackFunctions
{
    private readonly TableServiceClient _svc;
    private readonly ILogger _log;

    public ScheduleFeedbackFunctions(ILoggerFactory lf, TableServiceClient svc)
    {
        _svc = svc;
        _log = lf.CreateLogger<ScheduleFeedbackFunctions>();
    }

    [Function("SubmitScheduleFeedback")]
    public async Task<HttpResponseData> SubmitFeedback(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "schedule/feedback")] HttpRequestData req)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);

            var body = await HttpUtil.ReadJsonAsync<ScheduleFeedbackRequest>(req);
            if (body is null)
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, ErrorCodes.BAD_REQUEST, "Request body required.");

            var division = (body.division ?? "").Trim();
            if (string.IsNullOrWhiteSpace(division))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, ErrorCodes.BAD_REQUEST, "division is required.");

            // Store feedback in Table Storage
            var table = await TableClients.GetTableAsync(_svc, Constants.Tables.ScheduleFeedback);
            var timestamp = DateTimeOffset.UtcNow;
            var pk = $"FEEDBACK|{leagueId}|{division}";
            var rk = $"{timestamp:yyyy-MM-ddTHH:mm:ss.fff}|{Guid.NewGuid()}";

            var entity = new TableEntity(pk, rk)
            {
                ["LeagueId"] = leagueId,
                ["Division"] = division,
                ["UserId"] = me.UserId,
                ["Timestamp"] = timestamp,
                ["SelectedOption"] = body.selectedOption,
                ["TotalOptions"] = body.totalOptions,
                ["SelectedQuality"] = body.selectedQuality,
                ["SelectedMetrics"] = JsonSerializer.Serialize(body.selectedMetrics),
                ["AllOptionsMetrics"] = JsonSerializer.Serialize(body.allOptionsMetrics),
                ["UserComment"] = body.userComment ?? "",
                ["SeasonStart"] = body.seasonStart ?? "",
                ["SeasonEnd"] = body.seasonEnd ?? "",
                ["TeamCount"] = body.teamCount,
                ["MinGamesPerTeam"] = body.minGamesPerTeam,
                ["ConstructionStrategy"] = body.constructionStrategy ?? ""
            };

            await table.AddEntityAsync(entity);

            _log.LogInformation(
                "Schedule feedback captured: League={LeagueId}, Division={Division}, Option={Selected}/{Total}, Quality={Quality}",
                leagueId, division, body.selectedOption, body.totalOptions, body.selectedQuality);

            return ApiResponses.Ok(req, new { recorded = true });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error in SubmitScheduleFeedback");
            var requestId = req.FunctionContext.InvocationId.ToString();
            return ApiResponses.Error(
                req,
                HttpStatusCode.InternalServerError,
                ErrorCodes.INTERNAL_ERROR,
                "An error occurred while saving feedback.",
                new { requestId, exception = ex.GetType().Name });
        }
    }

    public record ScheduleFeedbackRequest(
        string? division,
        int selectedOption,
        int totalOptions,
        int selectedQuality,
        ScheduleMetrics? selectedMetrics,
        List<ScheduleOptionData>? allOptionsMetrics,
        string? userComment,
        string? seasonStart,
        string? seasonEnd,
        int teamCount,
        int minGamesPerTeam,
        string? constructionStrategy);

    public record ScheduleMetrics(
        int unassignedMatchups,
        int guestGamesScheduled,
        int hardIssues,
        int softScore,
        int doubleheaders,
        double teamLoadSpread,
        double pairDiversity,
        double dateSpreadStdDev,
        double guestSpread);

    public record ScheduleOptionData(
        int optionId,
        int quality,
        ScheduleMetrics metrics,
        bool wasSelected);
}
