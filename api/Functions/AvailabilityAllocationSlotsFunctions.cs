using System.Globalization;
using System.Net;
using Azure;
using Azure.Data.Tables;
using GameSwap.Functions.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace GameSwap.Functions.Functions;

public class AvailabilityAllocationSlotsFunctions
{
    private readonly ILogger _log;
    private readonly TableServiceClient _svc;

    public AvailabilityAllocationSlotsFunctions(ILoggerFactory lf, TableServiceClient tableServiceClient)
    {
        _log = lf.CreateLogger<AvailabilityAllocationSlotsFunctions>();
        _svc = tableServiceClient;
    }

    public record GenerateFromAllocationsRequest(
        string? division,
        string? dateFrom,
        string? dateTo,
        string? fieldKey
    );

    public record SlotCandidate(
        string gameDate,
        string startTime,
        string endTime,
        string fieldKey,
        string division
    );

    public record GenerateSlotsPreview(List<SlotCandidate> slots, List<SlotCandidate> conflicts);

    [Function("PreviewAllocationSlots")]
    public async Task<HttpResponseData> Preview(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "availability/allocations/slots/preview")] HttpRequestData req)
    {
        return await Generate(req, apply: false);
    }

    [Function("ApplyAllocationSlots")]
    public async Task<HttpResponseData> Apply(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "availability/allocations/slots/apply")] HttpRequestData req)
    {
        return await Generate(req, apply: true);
    }

    private async Task<HttpResponseData> Generate(HttpRequestData req, bool apply)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            await ApiGuards.RequireLeagueAdminAsync(_svc, me.UserId, leagueId);

            var body = await HttpUtil.ReadJsonAsync<GenerateFromAllocationsRequest>(req);
            if (body is null)
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "Invalid JSON body");

            var division = (body.division ?? "").Trim();
            var dateFrom = (body.dateFrom ?? "").Trim();
            var dateTo = (body.dateTo ?? "").Trim();
            var fieldKeyFilter = (body.fieldKey ?? "").Trim();

            if (string.IsNullOrWhiteSpace(division))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "division is required");
            ApiGuards.EnsureValidTableKeyPart("division", division);

            if (!DateOnly.TryParseExact(dateFrom, "yyyy-MM-dd", out var from))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "dateFrom must be YYYY-MM-DD.");
            if (!DateOnly.TryParseExact(dateTo, "yyyy-MM-dd", out var to))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "dateTo must be YYYY-MM-DD.");
            if (to < from)
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "dateTo must be on or after dateFrom.");

            var fieldsTable = await TableClients.GetTableAsync(_svc, Constants.Tables.Fields);
            var fieldMap = await LoadFieldMapAsync(fieldsTable, leagueId);

            if (!string.IsNullOrWhiteSpace(fieldKeyFilter) && !fieldMap.ContainsKey(fieldKeyFilter))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "fieldKey not found.");

            var allocations = await LoadAllocationsAsync(leagueId, division, fieldKeyFilter);
            if (allocations.Count == 0)
                return ApiResponses.Ok(req, new GenerateSlotsPreview(new List<SlotCandidate>(), new List<SlotCandidate>()));

            var leagueContext = await GetSeasonContextAsync(leagueId, division);
            if (leagueContext.gameLengthMinutes <= 0)
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "Season game length must be set for the league or division.");

            var candidates = new List<SlotCandidate>();
            foreach (var alloc in allocations)
            {
                if (!fieldMap.TryGetValue(alloc.fieldKey, out var fieldMeta)) continue;
                if (IsBlackoutRange(alloc.startsOn, alloc.endsOn, from, to) == false) continue;

                var days = NormalizeDays(alloc.daysOfWeek);
                var startMin = ParseMinutes(alloc.startTimeLocal);
                var endMin = ParseMinutes(alloc.endTimeLocal);
                if (startMin < 0 || endMin <= startMin) continue;

                var allocStart = DateOnly.ParseExact(alloc.startsOn, "yyyy-MM-dd");
                var allocEnd = DateOnly.ParseExact(alloc.endsOn, "yyyy-MM-dd");
                var windowStart = allocStart > from ? allocStart : from;
                var windowEnd = allocEnd < to ? allocEnd : to;

                for (var date = windowStart; date <= windowEnd; date = date.AddDays(1))
                {
                    if (days.Count > 0 && !days.Contains(date.DayOfWeek)) continue;
                    if (IsBlackout(date, leagueContext.blackouts)) continue;
                    if (IsBlackout(date, fieldMeta.blackouts)) continue;

                    var start = startMin;
                    while (start + leagueContext.gameLengthMinutes <= endMin)
                    {
                        var end = start + leagueContext.gameLengthMinutes;
                        candidates.Add(new SlotCandidate(
                            date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                            FormatTime(start),
                            FormatTime(end),
                            alloc.fieldKey,
                            division
                        ));
                        start = end;
                    }
                }
            }

            candidates = DeduplicateCandidates(candidates);

            var existingRanges = await LoadExistingSlotRangesAsync(leagueId, from, to);
            var conflicts = new List<SlotCandidate>();
            var toCreate = new List<SlotCandidate>();
            var newRanges = new Dictionary<string, List<(int startMin, int endMin)>>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in candidates)
            {
                var startMin = ParseMinutes(c.startTime);
                var endMin = ParseMinutes(c.endTime);
                if (startMin < 0 || endMin <= startMin)
                {
                    conflicts.Add(c);
                    continue;
                }

                var key = BuildRangeKey(c.fieldKey, c.gameDate);
                if (HasOverlap(existingRanges, key, startMin, endMin) || HasOverlap(newRanges, key, startMin, endMin))
                {
                    conflicts.Add(c);
                    continue;
                }

                AddRange(newRanges, key, startMin, endMin);
                toCreate.Add(c);
            }

            if (!apply)
                return ApiResponses.Ok(req, new GenerateSlotsPreview(toCreate, conflicts));

            var slotsTable = await TableClients.GetTableAsync(_svc, Constants.Tables.Slots);
            var created = new List<SlotCandidate>();
            foreach (var slot in toCreate)
            {
                if (!fieldMap.TryGetValue(slot.fieldKey, out var fieldMeta)) continue;
                var entity = BuildSlotEntity(leagueId, slot, fieldMeta);
                await slotsTable.AddEntityAsync(entity);
                created.Add(slot);
            }

            return ApiResponses.Ok(req, new { created, conflicts, skipped = conflicts.Count });
        }
        catch (ApiGuards.HttpError ex) { return ApiResponses.FromHttpError(req, ex); }
        catch (Exception ex)
        {
            _log.LogError(ex, "Generate allocation slots failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
        }
    }

    private record FieldMeta(string parkName, string fieldName, string displayName, List<(DateOnly start, DateOnly end)> blackouts);

    private record AllocationRow(
        string fieldKey,
        string startsOn,
        string endsOn,
        string daysOfWeek,
        string startTimeLocal,
        string endTimeLocal,
        bool isActive
    );

    private async Task<Dictionary<string, FieldMeta>> LoadFieldMapAsync(TableClient fieldsTable, string leagueId)
    {
        var map = new Dictionary<string, FieldMeta>(StringComparer.OrdinalIgnoreCase);
        var pkPrefix = $"FIELD|{leagueId}|";
        var next = pkPrefix + "\uffff";
        var filter = $"PartitionKey ge '{ApiGuards.EscapeOData(pkPrefix)}' and PartitionKey lt '{ApiGuards.EscapeOData(next)}'";

        await foreach (var e in fieldsTable.QueryAsync<TableEntity>(filter: filter))
        {
            var parkName = e.GetString("ParkName") ?? "";
            var fieldName = e.GetString("FieldName") ?? "";
            var displayName = e.GetString("DisplayName") ?? (string.IsNullOrWhiteSpace(parkName) || string.IsNullOrWhiteSpace(fieldName) ? "" : $"{parkName} > {fieldName}");
            var parkCode = e.GetString("ParkCode") ?? ExtractParkCodeFromPk(e.PartitionKey, leagueId);
            var fieldCode = e.GetString("FieldCode") ?? e.RowKey;
            if (string.IsNullOrWhiteSpace(parkCode) || string.IsNullOrWhiteSpace(fieldCode)) continue;

            var blackouts = ParseBlackouts(e.GetString("Blackouts"));
            map[$"{parkCode}/{fieldCode}"] = new FieldMeta(parkName, fieldName, displayName, blackouts);
        }

        return map;
    }

    private async Task<List<AllocationRow>> LoadAllocationsAsync(string leagueId, string division, string fieldKeyFilter)
    {
        var table = await TableClients.GetTableAsync(_svc, Constants.Tables.FieldAvailabilityAllocations);
        var list = new List<AllocationRow>();

        var scopes = new[] { division, "LEAGUE" };
        foreach (var scope in scopes)
        {
            var pkPrefix = $"ALLOC|{leagueId}|{scope}|";
            var next = pkPrefix + "\uffff";
            var filter = $"PartitionKey ge '{ApiGuards.EscapeOData(pkPrefix)}' and PartitionKey lt '{ApiGuards.EscapeOData(next)}'";
            if (!string.IsNullOrWhiteSpace(fieldKeyFilter))
                filter += $" and FieldKey eq '{ApiGuards.EscapeOData(fieldKeyFilter)}'";

            await foreach (var e in table.QueryAsync<TableEntity>(filter: filter))
            {
                if (!(e.GetBoolean("IsActive") ?? true)) continue;
                list.Add(new AllocationRow(
                    fieldKey: e.GetString("FieldKey") ?? "",
                    startsOn: e.GetString("StartsOn") ?? "",
                    endsOn: e.GetString("EndsOn") ?? "",
                    daysOfWeek: e.GetString("DaysOfWeek") ?? "",
                    startTimeLocal: e.GetString("StartTimeLocal") ?? "",
                    endTimeLocal: e.GetString("EndTimeLocal") ?? "",
                    isActive: e.GetBoolean("IsActive") ?? true
                ));
            }
        }

        return list;
    }

    private async Task<Dictionary<string, List<(int startMin, int endMin)>>> LoadExistingSlotRangesAsync(
        string leagueId,
        DateOnly from,
        DateOnly to)
    {
        var table = await TableClients.GetTableAsync(_svc, Constants.Tables.Slots);
        var prefix = $"SLOT|{leagueId}|";
        var next = prefix + "\uffff";
        var filter = $"PartitionKey ge '{ApiGuards.EscapeOData(prefix)}' and PartitionKey lt '{ApiGuards.EscapeOData(next)}' " +
                     $"and GameDate ge '{from:yyyy-MM-dd}' and GameDate le '{to:yyyy-MM-dd}'";

        var existing = new Dictionary<string, List<(int startMin, int endMin)>>(StringComparer.OrdinalIgnoreCase);
        await foreach (var e in table.QueryAsync<TableEntity>(filter: filter))
        {
            var status = (e.GetString("Status") ?? Constants.Status.SlotOpen).Trim();
            if (string.Equals(status, Constants.Status.SlotCancelled, StringComparison.OrdinalIgnoreCase))
                continue;

            var fieldKey = (e.GetString("FieldKey") ?? "").Trim();
            if (string.IsNullOrWhiteSpace(fieldKey)) continue;

            var gameDate = (e.GetString("GameDate") ?? "").Trim();
            var startTime = (e.GetString("StartTime") ?? "").Trim();
            var endTime = (e.GetString("EndTime") ?? "").Trim();
            var startMin = ParseMinutes(startTime);
            var endMin = ParseMinutes(endTime);
            if (startMin < 0 || endMin <= startMin) continue;

            var key = BuildRangeKey(fieldKey, gameDate);
            AddRange(existing, key, startMin, endMin);
        }

        return existing;
    }

    private async Task<(int gameLengthMinutes, List<(DateOnly start, DateOnly end)> blackouts)> GetSeasonContextAsync(string leagueId, string division)
    {
        var leagues = await TableClients.GetTableAsync(_svc, Constants.Tables.Leagues);
        var leagueEntity = (await leagues.GetEntityAsync<TableEntity>(Constants.Pk.Leagues, leagueId)).Value;
        var leagueGameLengthMinutes = leagueEntity.GetInt32("GameLengthMinutes") ?? 0;

        var blackouts = new List<(DateOnly start, DateOnly end)>();
        blackouts.AddRange(ParseBlackouts(leagueEntity.GetString("Blackouts")));

        var divisions = await TableClients.GetTableAsync(_svc, Constants.Tables.Divisions);
        try
        {
            var divEntity = (await divisions.GetEntityAsync<TableEntity>(Constants.Pk.Divisions(leagueId), division)).Value;
            var divisionGameLengthMinutes = divEntity.GetInt32("SeasonGameLengthMinutes") ?? 0;
            if (divisionGameLengthMinutes > 0)
                leagueGameLengthMinutes = divisionGameLengthMinutes;
            blackouts.AddRange(ParseBlackouts(divEntity.GetString("SeasonBlackouts")));
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // Ignore missing division overrides.
        }

        return (leagueGameLengthMinutes, blackouts);
    }

    private static List<(DateOnly start, DateOnly end)> ParseBlackouts(string? raw)
    {
        var blackouts = new List<(DateOnly start, DateOnly end)>();
        var blackoutsRaw = (raw ?? "").Trim();
        if (string.IsNullOrWhiteSpace(blackoutsRaw)) return blackouts;
        try
        {
            var parsed = System.Text.Json.JsonSerializer.Deserialize<List<BlackoutRange>>(blackoutsRaw) ?? new List<BlackoutRange>();
            foreach (var b in parsed)
            {
                if (!DateOnly.TryParseExact((b.startDate ?? "").Trim(), "yyyy-MM-dd", out var s)) continue;
                if (!DateOnly.TryParseExact((b.endDate ?? "").Trim(), "yyyy-MM-dd", out var eDate)) continue;
                if (eDate < s) continue;
                blackouts.Add((s, eDate));
            }
        }
        catch
        {
            return new List<(DateOnly start, DateOnly end)>();
        }

        return blackouts;
    }

    private static bool IsBlackout(DateOnly date, List<(DateOnly start, DateOnly end)> blackouts)
    {
        foreach (var (start, end) in blackouts)
        {
            if (date >= start && date <= end) return true;
        }
        return false;
    }

    private static bool IsBlackoutRange(string startsOn, string endsOn, DateOnly from, DateOnly to)
    {
        if (!DateOnly.TryParseExact(startsOn, "yyyy-MM-dd", out var start)) return false;
        if (!DateOnly.TryParseExact(endsOn, "yyyy-MM-dd", out var end)) return false;
        return !(end < from || start > to);
    }

    private static HashSet<DayOfWeek> NormalizeDays(string raw)
    {
        var set = new HashSet<DayOfWeek>();
        if (string.IsNullOrWhiteSpace(raw)) return set;
        foreach (var token in raw.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var key = token.Trim().ToLowerInvariant();
            if (key.StartsWith("sun")) set.Add(DayOfWeek.Sunday);
            else if (key.StartsWith("mon")) set.Add(DayOfWeek.Monday);
            else if (key.StartsWith("tue")) set.Add(DayOfWeek.Tuesday);
            else if (key.StartsWith("wed")) set.Add(DayOfWeek.Wednesday);
            else if (key.StartsWith("thu")) set.Add(DayOfWeek.Thursday);
            else if (key.StartsWith("fri")) set.Add(DayOfWeek.Friday);
            else if (key.StartsWith("sat")) set.Add(DayOfWeek.Saturday);
        }
        return set;
    }

    private static int ParseMinutes(string value)
    {
        var parts = (value ?? "").Split(':');
        if (parts.Length < 2) return -1;
        if (!int.TryParse(parts[0], out var h)) return -1;
        if (!int.TryParse(parts[1], out var m)) return -1;
        return h * 60 + m;
    }

    private static string FormatTime(int minutes)
    {
        var h = minutes / 60;
        var m = minutes % 60;
        return $"{h:D2}:{m:D2}";
    }

    private static string TimeKey(SlotCandidate s)
        => $"{s.gameDate}|{s.startTime}|{s.endTime}|{s.fieldKey}";

    private static string BuildRangeKey(string fieldKey, string gameDate)
        => $"{fieldKey}|{gameDate}";

    private static bool HasOverlap(Dictionary<string, List<(int startMin, int endMin)>> ranges, string key, int startMin, int endMin)
    {
        if (!ranges.TryGetValue(key, out var list)) return false;
        return list.Any(r => r.startMin < endMin && startMin < r.endMin);
    }

    private static void AddRange(Dictionary<string, List<(int startMin, int endMin)>> ranges, string key, int startMin, int endMin)
    {
        if (!ranges.TryGetValue(key, out var list))
        {
            list = new List<(int startMin, int endMin)>();
            ranges[key] = list;
        }

        list.Add((startMin, endMin));
    }

    private static List<SlotCandidate> DeduplicateCandidates(IEnumerable<SlotCandidate> candidates)
    {
        var list = new List<SlotCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            var key = TimeKey(candidate);
            if (seen.Add(key))
                list.Add(candidate);
        }
        return list;
    }

    private static TableEntity BuildSlotEntity(string leagueId, SlotCandidate slot, FieldMeta fieldMeta)
    {
        var now = DateTimeOffset.UtcNow;
        var pk = Constants.Pk.Slots(leagueId, slot.division);
        var slotId = Guid.NewGuid().ToString("N");
        return new TableEntity(pk, slotId)
        {
            ["LeagueId"] = leagueId,
            ["SlotId"] = slotId,
            ["Division"] = slot.division,
            ["OfferingTeamId"] = "",
            ["HomeTeamId"] = "",
            ["AwayTeamId"] = "",
            ["IsExternalOffer"] = false,
            ["IsAvailability"] = true,
            ["OfferingEmail"] = "",
            ["GameDate"] = slot.gameDate,
            ["StartTime"] = slot.startTime,
            ["EndTime"] = slot.endTime,
            ["ParkName"] = fieldMeta.parkName,
            ["FieldName"] = fieldMeta.fieldName,
            ["DisplayName"] = fieldMeta.displayName,
            ["FieldKey"] = slot.fieldKey,
            ["GameType"] = "Availability",
            ["Status"] = Constants.Status.SlotOpen,
            ["Notes"] = "Allocation",
            ["UpdatedUtc"] = now,
            ["LastUpdatedUtc"] = now
        };
    }

    private static string ExtractParkCodeFromPk(string pk, string leagueId)
    {
        var prefix = $"FIELD|{leagueId}|";
        if (!pk.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return "";
        return pk[prefix.Length..];
    }

    private static string ExtractDivision(string pk, string leagueId)
    {
        var prefix = $"SLOT|{leagueId}|";
        if (!pk.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return "";
        return pk[prefix.Length..];
    }

    private record BlackoutRange(string? startDate, string? endDate, string? label);
}
