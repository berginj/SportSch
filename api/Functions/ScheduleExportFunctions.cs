using System.Globalization;
using System.Net;
using Azure.Data.Tables;
using GameSwap.Functions.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace GameSwap.Functions.Functions;

public class ScheduleExportFunctions
{
    private readonly TableServiceClient _svc;
    private readonly ILogger _log;

    public ScheduleExportFunctions(ILoggerFactory lf, TableServiceClient svc)
    {
        _svc = svc;
        _log = lf.CreateLogger<ScheduleExportFunctions>();
    }

    [Function("ScheduleExportCsv")]
    public async Task<HttpResponseData> Export(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "schedule/export")] HttpRequestData req)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            await ApiGuards.RequireLeagueAdminAsync(_svc, me.UserId, leagueId);

            var division = (ApiGuards.GetQueryParam(req, "division") ?? "").Trim();
            if (string.IsNullOrWhiteSpace(division))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "division is required");
            ApiGuards.EnsureValidTableKeyPart("division", division);

            var dateFrom = (ApiGuards.GetQueryParam(req, "dateFrom") ?? "").Trim();
            var dateTo = (ApiGuards.GetQueryParam(req, "dateTo") ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(dateFrom) && !DateOnly.TryParseExact(dateFrom, "yyyy-MM-dd", out _))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "dateFrom must be YYYY-MM-DD.");
            if (!string.IsNullOrWhiteSpace(dateTo) && !DateOnly.TryParseExact(dateTo, "yyyy-MM-dd", out _))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "dateTo must be YYYY-MM-DD.");

            // Assumptions: default export is confirmed games only; filters can override via ?status=.
            var statusFilter = ParseStatusList((ApiGuards.GetQueryParam(req, "status") ?? "").Trim());
            if (statusFilter.Count == 0)
                statusFilter.Add(Constants.Status.SlotConfirmed);

            var fieldNames = await LoadFieldDisplayNamesAsync(leagueId);
            var rows = await LoadExportRowsAsync(leagueId, division, statusFilter, dateFrom, dateTo, fieldNames);

            var csv = ScheduleExportCsv.Build(rows);
            var resp = req.CreateResponse(HttpStatusCode.OK);
            resp.Headers.Add("Content-Type", "text/csv; charset=utf-8");
            resp.WriteString(csv);
            return resp;
        }
        catch (ApiGuards.HttpError ex)
        {
            return ApiResponses.FromHttpError(req, ex);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Schedule export failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
        }
    }

    private async Task<List<ScheduleExportRow>> LoadExportRowsAsync(
        string leagueId,
        string division,
        List<string> statusFilter,
        string dateFrom,
        string dateTo,
        Dictionary<string, string> fieldNames)
    {
        var table = await TableClients.GetTableAsync(_svc, Constants.Tables.Slots);
        var pk = Constants.Pk.Slots(leagueId, division);
        var filter = $"PartitionKey eq '{ApiGuards.EscapeOData(pk)}'";

        if (statusFilter.Count == 1)
            filter += $" and Status eq '{ApiGuards.EscapeOData(statusFilter.First())}'";
        if (!string.IsNullOrWhiteSpace(dateFrom))
            filter += $" and GameDate ge '{ApiGuards.EscapeOData(dateFrom)}'";
        if (!string.IsNullOrWhiteSpace(dateTo))
            filter += $" and GameDate le '{ApiGuards.EscapeOData(dateTo)}'";

        var rows = new List<ScheduleExportRow>();
        await foreach (var e in table.QueryAsync<TableEntity>(filter: filter))
        {
            var isAvailability = e.GetBoolean("IsAvailability") ?? false;
            if (isAvailability) continue;

            var status = (e.GetString("Status") ?? Constants.Status.SlotOpen).Trim();
            if (statusFilter.Count > 1 && !statusFilter.Contains(status, StringComparer.OrdinalIgnoreCase))
                continue;

            var homeTeam = (e.GetString("HomeTeamId") ?? e.GetString("OfferingTeamId") ?? "").Trim();
            // Away team can live on AwayTeamId or ConfirmedTeamId depending on workflow.
            var awayTeam = (e.GetString("AwayTeamId") ?? e.GetString("ConfirmedTeamId") ?? "").Trim();
            var gameDate = (e.GetString("GameDate") ?? "").Trim();
            var startTime = (e.GetString("StartTime") ?? "").Trim();
            var endTime = (e.GetString("EndTime") ?? "").Trim();
            var fieldKey = (e.GetString("FieldKey") ?? "").Trim();
            // Prefer canonical GameSwapFields display names for venue mapping.
            var venue = fieldNames.TryGetValue(fieldKey, out var displayName)
                ? displayName
                : (e.GetString("DisplayName") ?? fieldKey).Trim();

            rows.Add(new ScheduleExportRow(
                EventType: "Game",
                Date: gameDate,
                StartTime: startTime,
                EndTime: endTime,
                Duration: FormatDuration(startTime, endTime),
                HomeTeam: homeTeam,
                AwayTeam: awayTeam,
                Venue: venue,
                Status: MapStatus(status)
            ));
        }

        return rows
            .OrderBy(r => r.Date)
            .ThenBy(r => r.StartTime)
            .ThenBy(r => r.Venue)
            .ToList();
    }

    private async Task<Dictionary<string, string>> LoadFieldDisplayNamesAsync(string leagueId)
    {
        var table = await TableClients.GetTableAsync(_svc, Constants.Tables.Fields);
        var pkPrefix = $"FIELD|{leagueId}|";
        var next = pkPrefix + "\uffff";
        var filter = $"PartitionKey ge '{ApiGuards.EscapeOData(pkPrefix)}' and PartitionKey lt '{ApiGuards.EscapeOData(next)}'";

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await foreach (var e in table.QueryAsync<TableEntity>(filter: filter))
        {
            var parkCode = ExtractParkCodeFromPk(e.PartitionKey, leagueId);
            var fieldCode = e.RowKey;
            if (string.IsNullOrWhiteSpace(parkCode) || string.IsNullOrWhiteSpace(fieldCode)) continue;

            var parkName = (e.GetString("ParkName") ?? "").Trim();
            var fieldName = (e.GetString("FieldName") ?? "").Trim();
            var displayName = (e.GetString("DisplayName") ?? "").Trim();
            if (string.IsNullOrWhiteSpace(displayName) && (!string.IsNullOrWhiteSpace(parkName) || !string.IsNullOrWhiteSpace(fieldName)))
                displayName = $"{parkName} > {fieldName}".Trim();

            map[$"{parkCode}/{fieldCode}"] = displayName;
        }

        return map;
    }

    private static string ExtractParkCodeFromPk(string pk, string leagueId)
    {
        var prefix = $"FIELD|{leagueId}|";
        if (!pk.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return "";
        return pk[prefix.Length..];
    }

    private static string FormatDuration(string start, string end)
    {
        var duration = CalcDurationMinutes(start, end);
        return duration.HasValue ? duration.Value.ToString(CultureInfo.InvariantCulture) : "";
    }

    private static int? CalcDurationMinutes(string start, string end)
    {
        var s = ParseTimeMinutes(start);
        var e = ParseTimeMinutes(end);
        if (!s.HasValue || !e.HasValue || e <= s) return null;
        return e - s;
    }

    private static int? ParseTimeMinutes(string raw)
    {
        var parts = (raw ?? "").Split(":");
        if (parts.Length < 2) return null;
        if (!int.TryParse(parts[0], out var h)) return null;
        if (!int.TryParse(parts[1], out var m)) return null;
        return h * 60 + m;
    }

    private static string MapStatus(string status)
    {
        if (string.Equals(status, Constants.Status.SlotConfirmed, StringComparison.OrdinalIgnoreCase))
            return Constants.Status.EventScheduled;
        if (string.Equals(status, Constants.Status.SlotCancelled, StringComparison.OrdinalIgnoreCase))
            return Constants.Status.EventCancelled;
        return status;
    }

    private static List<string> ParseStatusList(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new List<string>();
        return raw
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
