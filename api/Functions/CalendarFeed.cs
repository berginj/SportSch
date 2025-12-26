using System.Net;
using System.Text;
using System.Linq;
using Azure.Data.Tables;
using GameSwap.Functions.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace GameSwap.Functions.Functions;

public class CalendarFeed
{
    private readonly ILogger _log;
    private readonly TableServiceClient _svc;

    private const string SlotsTableName = Constants.Tables.Slots;
    private const string EventsTableName = Constants.Tables.Events;

    public CalendarFeed(ILoggerFactory lf, TableServiceClient tableServiceClient)
    {
        _log = lf.CreateLogger<CalendarFeed>();
        _svc = tableServiceClient;
    }

    private record CalendarItem(
        string kind,
        string id,
        string title,
        string description,
        string status,
        string startDate,
        string startTime,
        string endDate,
        string endTime,
        string location
    );

    [Function("CalendarFeed")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "calendar/ics")] HttpRequestData req)
    {
        try
        {
            var leagueId = GetLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            await ApiGuards.RequireMemberAsync(_svc, me.UserId, leagueId);

            var division = (ApiGuards.GetQueryParam(req, "division") ?? "").Trim();
            var dateFrom = (ApiGuards.GetQueryParam(req, "dateFrom") ?? "").Trim();
            var dateTo = (ApiGuards.GetQueryParam(req, "dateTo") ?? "").Trim();

            var includeSlots = GetBoolQuery(req, "includeSlots", defaultValue: true);
            var includeEvents = GetBoolQuery(req, "includeEvents", defaultValue: true);

            var statusRaw = (ApiGuards.GetQueryParam(req, "status") ?? ApiGuards.GetQueryParam(req, "slotStatus") ?? "").Trim();
            var includeCancelled = GetBoolQuery(req, "includeCancelled", defaultValue: false);
            var slotStatuses = ParseStatusList(statusRaw);
            if (slotStatuses.Count == 0)
            {
                slotStatuses.Add(Constants.Status.SlotOpen);
                slotStatuses.Add(Constants.Status.SlotConfirmed);
                if (includeCancelled) slotStatuses.Add(Constants.Status.SlotCancelled);
            }

            var items = new List<CalendarItem>();

            if (includeEvents)
            {
                var eventsTable = await TableClients.GetTableAsync(_svc, EventsTableName);
                var pk = Constants.Pk.Events(leagueId);
                var filter = $"PartitionKey eq '{ApiGuards.EscapeOData(pk)}'";

                if (!string.IsNullOrWhiteSpace(division))
                    filter += $" and (Division eq '{ApiGuards.EscapeOData(division)}' or Division eq '')";
                if (!string.IsNullOrWhiteSpace(dateFrom))
                    filter += $" and EventDate ge '{ApiGuards.EscapeOData(dateFrom)}'";
                if (!string.IsNullOrWhiteSpace(dateTo))
                    filter += $" and EventDate le '{ApiGuards.EscapeOData(dateTo)}'";

                await foreach (var e in eventsTable.QueryAsync<TableEntity>(filter: filter))
                {
                    var title = (e.GetString("Title") ?? "").Trim();
                    var type = (e.GetString("Type") ?? "").Trim();
                    var status = (e.GetString("Status") ?? "").Trim();
                    var eventDate = (e.GetString("EventDate") ?? "").Trim();
                    var startTime = (e.GetString("StartTime") ?? "").Trim();
                    var endTime = (e.GetString("EndTime") ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(eventDate) || string.IsNullOrWhiteSpace(startTime) || string.IsNullOrWhiteSpace(endTime))
                        continue;

                    var divisionLabel = (e.GetString("Division") ?? "").Trim();
                    var teamId = (e.GetString("TeamId") ?? "").Trim();
                    var opponent = (e.GetString("OpponentTeamId") ?? "").Trim();
                    var location = (e.GetString("Location") ?? "").Trim();
                    var notes = (e.GetString("Notes") ?? "").Trim();

                    var summary = string.IsNullOrWhiteSpace(type) ? title : $"{type}: {title}";
                    var descParts = new List<string>();
                    if (!string.IsNullOrWhiteSpace(status)) descParts.Add($"Status: {status}");
                    if (!string.IsNullOrWhiteSpace(divisionLabel)) descParts.Add($"Division: {divisionLabel}");
                    if (!string.IsNullOrWhiteSpace(teamId)) descParts.Add($"Team: {teamId}");
                    if (!string.IsNullOrWhiteSpace(opponent)) descParts.Add($"Opponent: {opponent}");
                    if (!string.IsNullOrWhiteSpace(notes)) descParts.Add(notes);

                    items.Add(new CalendarItem(
                        kind: "event",
                        id: e.RowKey,
                        title: summary,
                        description: string.Join(" | ", descParts),
                        status: status,
                        startDate: eventDate,
                        startTime: startTime,
                        endDate: eventDate,
                        endTime: endTime,
                        location: location
                    ));
                }
            }

            if (includeSlots)
            {
                var slotsTable = await TableClients.GetTableAsync(_svc, SlotsTableName);
                var filter = BuildSlotFilter(leagueId, division, dateFrom, dateTo, slotStatuses);

                await foreach (var s in slotsTable.QueryAsync<TableEntity>(filter: filter))
                {
                    var status = (s.GetString("Status") ?? Constants.Status.SlotOpen).Trim();
                    if (!slotStatuses.Contains(status, StringComparer.OrdinalIgnoreCase))
                        continue;

                    var gameDate = (s.GetString("GameDate") ?? "").Trim();
                    var startTime = (s.GetString("StartTime") ?? "").Trim();
                    var endTime = (s.GetString("EndTime") ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(gameDate) || string.IsNullOrWhiteSpace(startTime) || string.IsNullOrWhiteSpace(endTime))
                        continue;

                    var offeringTeamId = (s.GetString("OfferingTeamId") ?? "").Trim();
                    var confirmedTeamId = (s.GetString("ConfirmedTeamId") ?? "").Trim();
                    var parkName = (s.GetString("ParkName") ?? "").Trim();
                    var fieldName = (s.GetString("FieldName") ?? "").Trim();
                    var displayName = (s.GetString("DisplayName") ?? "").Trim();
                    var location = string.IsNullOrWhiteSpace(displayName) ? $"{parkName} {fieldName}".Trim() : displayName;

                    var summary = $"{offeringTeamId} @ {location}".Trim();
                    var descParts = new List<string>();
                    if (!string.IsNullOrWhiteSpace(status)) descParts.Add($"Status: {status}");
                    if (!string.IsNullOrWhiteSpace(confirmedTeamId)) descParts.Add($"Confirmed: {confirmedTeamId}");
                    var notes = (s.GetString("Notes") ?? "").Trim();
                    if (!string.IsNullOrWhiteSpace(notes)) descParts.Add(notes);

                    items.Add(new CalendarItem(
                        kind: "slot",
                        id: s.RowKey,
                        title: summary,
                        description: string.Join(" | ", descParts),
                        status: status,
                        startDate: gameDate,
                        startTime: startTime,
                        endDate: gameDate,
                        endTime: endTime,
                        location: location
                    ));
                }
            }

            var ics = BuildIcs(items.OrderBy(x => x.startDate).ThenBy(x => x.startTime), leagueId);
            var res = req.CreateResponse(HttpStatusCode.OK);
            res.Headers.Add("Content-Type", "text/calendar; charset=utf-8");
            await res.WriteStringAsync(ics);
            return res;
        }
        catch (ApiGuards.HttpError ex) { return ApiResponses.FromHttpError(req, ex); }
        catch (Exception ex)
        {
            _log.LogError(ex, "CalendarFeed failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
        }
    }

    private static string GetLeagueId(HttpRequestData req)
    {
        if (req.Headers.TryGetValues("x-league-id", out var vals))
        {
            var header = vals.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(header)) return header.Trim();
        }

        var q = (ApiGuards.GetQueryParam(req, "leagueId") ?? "").Trim();
        if (string.IsNullOrWhiteSpace(q))
            throw new ApiGuards.HttpError((int)HttpStatusCode.BadRequest, "Missing x-league-id header.");
        return q;
    }

    private static bool GetBoolQuery(HttpRequestData req, string key, bool defaultValue)
    {
        var v = ApiGuards.GetQueryParam(req, key);
        if (string.IsNullOrWhiteSpace(v)) return defaultValue;
        return bool.TryParse(v, out var b) ? b : defaultValue;
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

    private static string BuildSlotFilter(string leagueId, string division, string dateFrom, string dateTo, List<string> statuses)
    {
        var filter = "";
        if (!string.IsNullOrWhiteSpace(division))
        {
            ApiGuards.EnsureValidTableKeyPart("division", division);
            var pk = $"SLOT|{leagueId}|{division}";
            filter = $"PartitionKey eq '{ApiGuards.EscapeOData(pk)}'";
        }
        else
        {
            var prefix = $"SLOT|{leagueId}|";
            filter = $"PartitionKey ge '{ApiGuards.EscapeOData(prefix)}' and PartitionKey lt '{ApiGuards.EscapeOData(prefix + "~")}'";
        }

        if (statuses.Count == 1)
            filter += $" and Status eq '{ApiGuards.EscapeOData(statuses[0])}'";

        if (!string.IsNullOrWhiteSpace(dateFrom))
            filter += $" and GameDate ge '{ApiGuards.EscapeOData(dateFrom)}'";
        if (!string.IsNullOrWhiteSpace(dateTo))
            filter += $" and GameDate le '{ApiGuards.EscapeOData(dateTo)}'";

        return filter;
    }

    private static string BuildIcs(IEnumerable<CalendarItem> items, string leagueId)
    {
        var sb = new StringBuilder();
        sb.AppendLine("BEGIN:VCALENDAR");
        sb.AppendLine("VERSION:2.0");
        sb.AppendLine("PRODID:-//GameSwap//Calendar//EN");
        sb.AppendLine("CALSCALE:GREGORIAN");
        sb.AppendLine("X-WR-CALNAME:GameSwap " + EscapeIcs(leagueId));
        sb.AppendLine("X-WR-TIMEZONE:America/New_York");

        foreach (var item in items)
        {
            if (!TryFormatDateTime(item.startDate, item.startTime, out var start)) continue;
            if (!TryFormatDateTime(item.endDate, item.endTime, out var end)) continue;

            sb.AppendLine("BEGIN:VEVENT");
            sb.AppendLine("UID:" + EscapeIcs($"{item.kind}-{item.id}@gameswap"));
            sb.AppendLine("DTSTAMP:" + DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'"));
            sb.AppendLine($"DTSTART;TZID=America/New_York:{start}");
            sb.AppendLine($"DTEND;TZID=America/New_York:{end}");
            sb.AppendLine("SUMMARY:" + EscapeIcs(item.title));
            if (!string.IsNullOrWhiteSpace(item.description))
                sb.AppendLine("DESCRIPTION:" + EscapeIcs(item.description));
            if (!string.IsNullOrWhiteSpace(item.location))
                sb.AppendLine("LOCATION:" + EscapeIcs(item.location));
            if (!string.IsNullOrWhiteSpace(item.status))
                sb.AppendLine("STATUS:" + EscapeIcs(item.status));
            sb.AppendLine("END:VEVENT");
        }

        sb.AppendLine("END:VCALENDAR");
        return sb.ToString();
    }

    private static bool TryFormatDateTime(string date, string time, out string formatted)
    {
        formatted = "";
        if (!DateTime.TryParse($"{date} {time}", out var dt)) return false;
        formatted = dt.ToString("yyyyMMdd'T'HHmmss");
        return true;
    }

    private static string EscapeIcs(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        return s.Replace("\\", "\\\\")
            .Replace(";", "\\;")
            .Replace(",", "\\,")
            .Replace("\r\n", "\\n")
            .Replace("\n", "\\n");
    }
}
