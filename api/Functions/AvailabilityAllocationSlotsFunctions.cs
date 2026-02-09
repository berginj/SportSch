using System.Globalization;
using System.Net;
using Azure;
using Azure.Data.Tables;
using GameSwap.Functions.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using GameSwap.Functions.Telemetry;

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
        string division,
        string slotType,
        int? priorityRank
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
        var stage = "init";
        try
        {
            stage = "auth";
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            await ApiGuards.RequireLeagueAdminAsync(_svc, me.UserId, leagueId);

            stage = "read_body";
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

            stage = "load_fields";
            var fieldsTable = await TableClients.GetTableAsync(_svc, Constants.Tables.Fields);
            var fieldMap = await LoadFieldMapAsync(fieldsTable, leagueId);

            if (!string.IsNullOrWhiteSpace(fieldKeyFilter) && !fieldMap.ContainsKey(fieldKeyFilter))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "fieldKey not found.");

            stage = "load_allocations";
            var allocations = await LoadAllocationsAsync(leagueId, division, fieldKeyFilter);
            if (allocations.Count == 0)
                return ApiResponses.Ok(req, new GenerateSlotsPreview(new List<SlotCandidate>(), new List<SlotCandidate>()));

            stage = "load_season_context";
            var leagueContext = await GetSeasonContextAsync(leagueId, division);
            if (leagueContext.gameLengthMinutes <= 0)
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "Season game length must be set for the league or division.");

            stage = "load_existing_ranges";
            var existingRanges = await LoadExistingSlotRangesAsync(leagueId, from, to);

            const int MaxResponseItems = 200;
            var slotSamples = new List<SlotCandidate>();
            var conflictSamples = new List<SlotCandidate>();
            var failed = new List<object>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var newRanges = new Dictionary<string, List<(int startMin, int endMin)>>(StringComparer.OrdinalIgnoreCase);
            var slotCount = 0;
            var conflictCount = 0;
            var failedCount = 0;

            TableClient? slotsTable = null;
            if (apply)
            {
                stage = "load_slots_table";
                slotsTable = await TableClients.GetTableAsync(_svc, Constants.Tables.Slots);
            }

            stage = "generate";
            foreach (var alloc in allocations)
            {
                if (!fieldMap.TryGetValue(alloc.fieldKey, out var fieldMeta)) continue;
                if (IsBlackoutRange(alloc.startsOn, alloc.endsOn, from, to) == false) continue;

                if (!DateOnly.TryParseExact(alloc.startsOn, "yyyy-MM-dd", out var allocStart)) continue;
                if (!DateOnly.TryParseExact(alloc.endsOn, "yyyy-MM-dd", out var allocEnd)) continue;
                var days = NormalizeDays(alloc.daysOfWeek);
                var startMin = SlotOverlap.ParseMinutes(alloc.startTimeLocal);
                var endMin = SlotOverlap.ParseMinutes(alloc.endTimeLocal);
                if (startMin < 0 || endMin <= startMin) continue;
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
                        var candidate = new SlotCandidate(
                            date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                            FormatTime(start),
                            FormatTime(end),
                            alloc.fieldKey,
                            division,
                            alloc.slotType,
                            alloc.priorityRank
                        );

                        if (!seen.Add(TimeKey(candidate)))
                        {
                            start = end;
                            continue;
                        }

                        var key = SlotOverlap.BuildRangeKey(candidate.fieldKey, candidate.gameDate);
                        if (SlotOverlap.HasOverlap(existingRanges, key, start, end) || SlotOverlap.HasOverlap(newRanges, key, start, end))
                        {
                            conflictCount++;
                            if (conflictSamples.Count < MaxResponseItems)
                                conflictSamples.Add(candidate);
                            start = end;
                            continue;
                        }

                        SlotOverlap.AddRange(newRanges, key, start, end);

                        if (!apply)
                        {
                            slotCount++;
                            if (slotSamples.Count < MaxResponseItems)
                                slotSamples.Add(candidate);
                        }
                        else
                        {
                            var entity = BuildSlotEntity(leagueId, candidate, fieldMeta);
                            try
                            {
                                await slotsTable!.AddEntityAsync(entity);
                                slotCount++;
                                if (slotSamples.Count < MaxResponseItems)
                                    slotSamples.Add(candidate);
                            }
                            catch (RequestFailedException ex)
                            {
                                _log.LogWarning(ex, "Create allocation slot failed for {fieldKey} {gameDate} {startTime}-{endTime}",
                                    candidate.fieldKey, candidate.gameDate, candidate.startTime, candidate.endTime);
                                failedCount++;
                                if (failed.Count < MaxResponseItems)
                                {
                                    failed.Add(new
                                    {
                                        candidate.gameDate,
                                        candidate.startTime,
                                        candidate.endTime,
                                        candidate.fieldKey,
                                        candidate.division,
                                        status = ex.Status,
                                        code = ex.ErrorCode
                                    });
                                }
                            }
                        }

                        start = end;
                    }
                }
            }

            if (!apply)
            {
                UsageTelemetry.Track(_log, "api_availability_allocations_slots_preview", leagueId, me.UserId, new
                {
                    division,
                    slots = slotCount,
                    conflicts = conflictCount
                });
                return ApiResponses.Ok(req, new
                {
                    slots = slotSamples,
                    conflicts = conflictSamples,
                    slotCount,
                    conflictCount
                });
            }

            UsageTelemetry.Track(_log, "api_availability_allocations_slots_apply", leagueId, me.UserId, new
            {
                division,
                created = slotCount,
                conflicts = conflictCount,
                failed = failedCount
            });

            return ApiResponses.Ok(req, new
            {
                created = slotSamples,
                conflicts = conflictSamples,
                failed,
                createdCount = slotCount,
                conflictCount,
                failedCount,
                skipped = conflictCount + failedCount
            });
        }
        catch (ApiGuards.HttpError ex) { return ApiResponses.FromHttpError(req, ex); }
        catch (RequestFailedException ex)
        {
            _log.LogError(ex, "Generate allocation slots storage request failed");
            var requestId = req.FunctionContext.InvocationId.ToString();
            return ApiResponses.Error(req, HttpStatusCode.BadGateway, "STORAGE_ERROR", "Storage request failed",
                new { requestId, status = ex.Status, code = ex.ErrorCode });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Generate allocation slots failed");
            var requestId = req.FunctionContext.InvocationId.ToString();
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, ErrorCodes.INTERNAL_ERROR, "Internal Server Error",
                new { requestId, stage, exception = ex.GetType().Name, message = ex.Message });
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
        string slotType,
        int? priorityRank,
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
                if (!ReadBool(e, "IsActive", true)) continue;
                list.Add(new AllocationRow(
                    fieldKey: ReadString(e, "FieldKey"),
                    startsOn: ReadString(e, "StartsOn"),
                    endsOn: ReadString(e, "EndsOn"),
                    daysOfWeek: ReadString(e, "DaysOfWeek"),
                    startTimeLocal: ReadString(e, "StartTimeLocal"),
                    endTimeLocal: ReadString(e, "EndTimeLocal"),
                    slotType: NormalizeSlotType(ReadString(e, "SlotType")),
                    priorityRank: ParsePriorityRank(ReadObject(e, "PriorityRank")),
                    isActive: ReadBool(e, "IsActive", true)
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
            var startMin = SlotOverlap.ParseMinutes(startTime);
            var endMin = SlotOverlap.ParseMinutes(endTime);
            if (startMin < 0 || endMin <= startMin) continue;

            var key = SlotOverlap.BuildRangeKey(fieldKey, gameDate);
            SlotOverlap.AddRange(existing, key, startMin, endMin);
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

    private static string FormatTime(int minutes)
    {
        var h = minutes / 60;
        var m = minutes % 60;
        return $"{h:D2}:{m:D2}";
    }

    private static string TimeKey(SlotCandidate s)
        => $"{s.gameDate}|{s.startTime}|{s.endTime}|{s.fieldKey}";

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
        var entity = new TableEntity(pk, slotId)
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
            ["AllocationSlotType"] = slot.slotType,
            ["Status"] = Constants.Status.SlotOpen,
            ["Notes"] = "Allocation",
            ["UpdatedUtc"] = now,
            ["LastUpdatedUtc"] = now
        };
        if (slot.priorityRank.HasValue)
            entity["AllocationPriorityRank"] = slot.priorityRank.Value;
        return entity;
    }

    private static string NormalizeSlotType(string? raw)
    {
        var key = (raw ?? "").Trim().ToLowerInvariant();
        if (key == "game") return "game";
        if (key == "both") return "both";
        return "practice";
    }

    private static int? ParsePriorityRank(object? rawValue)
    {
        if (rawValue is null) return null;
        if (rawValue is int i && i > 0) return i;
        if (rawValue is long l && l > 0 && l <= int.MaxValue) return (int)l;
        if (rawValue is double d && d > 0)
        {
            var rounded = (int)Math.Round(d);
            return rounded > 0 ? rounded : null;
        }
        var raw = rawValue.ToString()?.Trim() ?? "";
        if (int.TryParse(raw, out var parsed) && parsed > 0) return parsed;
        return null;
    }

    private static object? ReadObject(TableEntity entity, string key)
        => entity.TryGetValue(key, out var value) ? value : null;

    private static string ReadString(TableEntity entity, string key)
    {
        if (!entity.TryGetValue(key, out var value) || value is null) return "";
        return value.ToString()?.Trim() ?? "";
    }

    private static bool ReadBool(TableEntity entity, string key, bool defaultValue)
    {
        if (!entity.TryGetValue(key, out var value) || value is null) return defaultValue;
        if (value is bool b) return b;
        if (bool.TryParse(value.ToString(), out var parsed)) return parsed;
        return defaultValue;
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
