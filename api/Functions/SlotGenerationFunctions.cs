using System.Globalization;
using System.Linq;
using System.Net;
using Azure;
using Azure.Data.Tables;
using GameSwap.Functions.Storage;
using GameSwap.Functions.Scheduling;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace GameSwap.Functions.Functions;

public class SlotGenerationFunctions
{
    private readonly TableServiceClient _svc;
    private readonly ILogger _log;

    public SlotGenerationFunctions(ILoggerFactory lf, TableServiceClient svc)
    {
        _svc = svc;
        _log = lf.CreateLogger<SlotGenerationFunctions>();
    }

    public record GenerateSlotsReq(
        string? division,
        string? fieldKey,
        string? dateFrom,
        string? dateTo,
        List<string>? daysOfWeek,
        string? startTime,
        string? endTime,
        string? source
    );

    public record SlotCandidate(
        string gameDate,
        string startTime,
        string endTime,
        string fieldKey,
        string division
    );

    public record GenerateSlotsPreview(List<SlotCandidate> slots, List<SlotCandidate> conflicts);

    [Function("PreviewGeneratedSlots")]
    public async Task<HttpResponseData> Preview(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "schedule/slots/preview")] HttpRequestData req)
    {
        return await Generate(req, applyMode: "");
    }

    [Function("ApplyGeneratedSlots")]
    public async Task<HttpResponseData> Apply(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "schedule/slots/apply")] HttpRequestData req)
    {
        var mode = (ApiGuards.GetQueryParam(req, "mode") ?? "skip").Trim().ToLowerInvariant();
        return await Generate(req, applyMode: mode);
    }

    private async Task<HttpResponseData> Generate(HttpRequestData req, string applyMode)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            await ApiGuards.RequireLeagueAdminAsync(_svc, me.UserId, leagueId);

            var body = await HttpUtil.ReadJsonAsync<GenerateSlotsReq>(req);
            if (body is null)
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "Invalid JSON body");

            var division = (body.division ?? "").Trim();
            var fieldKey = (body.fieldKey ?? "").Trim();
            var dateFrom = (body.dateFrom ?? "").Trim();
            var dateTo = (body.dateTo ?? "").Trim();
            var startTime = (body.startTime ?? "").Trim();
            var endTime = (body.endTime ?? "").Trim();
            var source = (body.source ?? "").Trim().ToLowerInvariant();
            var useRules = string.Equals(source, "rules", StringComparison.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(division) || string.IsNullOrWhiteSpace(fieldKey))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "division and fieldKey are required");
            ApiGuards.EnsureValidTableKeyPart("division", division);

            if (!DateOnly.TryParseExact(dateFrom, "yyyy-MM-dd", out var from))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "dateFrom must be YYYY-MM-DD.");
            if (!DateOnly.TryParseExact(dateTo, "yyyy-MM-dd", out var to))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "dateTo must be YYYY-MM-DD.");
            if (to < from)
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "dateTo must be on or after dateFrom.");
            var startMin = 0;
            var endMin = 0;
            var days = new HashSet<DayOfWeek>();
            if (!useRules)
            {
                if (!TimeUtil.IsValidRange(startTime, endTime, out startMin, out endMin, out var timeErr))
                    return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", timeErr);

                days = NormalizeDays(body.daysOfWeek);
                if (days.Count == 0)
                    return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "daysOfWeek is required");
            }

            if (!TryParseFieldKey(fieldKey, out var parkCode, out var fieldCode))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "fieldKey must be parkCode/fieldCode.");

            var fields = await TableClients.GetTableAsync(_svc, Constants.Tables.Fields);
            TableEntity field;
            try
            {
                field = (await fields.GetEntityAsync<TableEntity>(Constants.Pk.Fields(leagueId, parkCode), fieldCode)).Value;
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "Field not found.");
            }
            var parkName = (field.GetString("ParkName") ?? "").Trim();
            var fieldName = (field.GetString("FieldName") ?? "").Trim();
            var displayName = (field.GetString("DisplayName") ?? "").Trim();

            var season = await GetSeasonContextAsync(leagueId, division, field);
            var gameLengthMinutes = season.gameLengthMinutes;
            if (gameLengthMinutes <= 0)
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "Season game length must be set for the league or division.");

            var blackoutRanges = season.blackouts;
            var candidateSlots = useRules
                ? await BuildRuleBasedSlotsAsync(leagueId, fieldKey, division, from, to, gameLengthMinutes, blackoutRanges)
                : BuildCandidateSlots(from, to, days, startMin, endMin, gameLengthMinutes, fieldKey, division, blackoutRanges);

            var existingRanges = await LoadExistingSlotRangesAsync(leagueId, fieldKey, from, to, includeAvailability: true);
            var (conflicts, toCreate) = SplitByOverlap(candidateSlots, existingRanges);

            if (string.IsNullOrWhiteSpace(applyMode))
            {
                return ApiResponses.Ok(req, new GenerateSlotsPreview(toCreate, conflicts));
            }

            var created = new List<SlotCandidate>();
            var skipped = new List<SlotCandidate>();
            var overwritten = new List<SlotCandidate>();

            if (applyMode != "skip" && applyMode != "overwrite" && applyMode != "regenerate")
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "mode must be skip, overwrite, or regenerate");

            var slotsTable = await TableClients.GetTableAsync(_svc, Constants.Tables.Slots);
            var cleared = 0;
            if (applyMode == "regenerate")
            {
                cleared = await ClearAvailabilitySlotsAsync(slotsTable, leagueId, division, fieldKey, from, to);
                existingRanges = await LoadExistingSlotRangesAsync(leagueId, fieldKey, from, to, includeAvailability: false);
                (conflicts, toCreate) = SplitByOverlap(candidateSlots, existingRanges);
            }
            foreach (var slot in toCreate)
            {
                var entity = BuildSlotEntity(leagueId, slot, gameLengthMinutes, parkName, fieldName, displayName);
                await slotsTable.AddEntityAsync(entity);
                created.Add(slot);
            }

            if (applyMode == "overwrite" && conflicts.Any())
            {
                await OverwriteConflictsAsync(slotsTable, leagueId, fieldKey, conflicts, created, overwritten, skipped);
            }
            else
            {
                skipped.AddRange(conflicts);
            }

            return ApiResponses.Ok(req, new
            {
                created,
                overwritten,
                skipped,
                cleared
            });
        }
        catch (ApiGuards.HttpError ex)
        {
            return ApiResponses.FromHttpError(req, ex);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "GenerateSlots failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
        }
    }

    private record SeasonContext(int gameLengthMinutes, List<(DateOnly start, DateOnly end)> blackouts);
    private record BlackoutRange(string? startDate, string? endDate, string? label);

    private async Task<SeasonContext> GetSeasonContextAsync(string leagueId, string division, TableEntity field)
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

        blackouts.AddRange(ParseBlackouts(field.GetString("Blackouts")));

        return new SeasonContext(leagueGameLengthMinutes, blackouts);
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

    private static HashSet<DayOfWeek> NormalizeDays(List<string>? days)
    {
        var set = new HashSet<DayOfWeek>();
        if (days is null) return set;
        foreach (var raw in days)
        {
            var key = (raw ?? "").Trim().ToLowerInvariant();
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

    private static List<SlotCandidate> BuildCandidateSlots(
        DateOnly from,
        DateOnly to,
        HashSet<DayOfWeek> days,
        int startMin,
        int endMin,
        int gameLengthMinutes,
        string fieldKey,
        string division,
        List<(DateOnly start, DateOnly end)> blackouts)
    {
        var list = new List<SlotCandidate>();
        for (var date = from; date <= to; date = date.AddDays(1))
        {
            if (!days.Contains(date.DayOfWeek)) continue;
            if (IsBlackout(date, blackouts)) continue;

            var start = startMin;
            while (start + gameLengthMinutes <= endMin)
            {
                var end = start + gameLengthMinutes;
                list.Add(new SlotCandidate(
                    date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    FormatTime(start),
                    FormatTime(end),
                    fieldKey,
                    division
                ));
                start = end;
            }
        }
        return list;
    }

    private static bool IsBlackout(DateOnly date, List<(DateOnly start, DateOnly end)> blackouts)
    {
        foreach (var (start, end) in blackouts)
        {
            if (date >= start && date <= end) return true;
        }
        return false;
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

    private static bool TryParseMinutesRange(string startTime, string endTime, out int startMin, out int endMin)
    {
        startMin = ParseMinutes(startTime);
        endMin = ParseMinutes(endTime);
        return startMin >= 0 && endMin > startMin;
    }

    private static int ParseMinutes(string value)
    {
        var parts = (value ?? "").Split(':');
        if (parts.Length < 2) return -1;
        if (!int.TryParse(parts[0], out var h)) return -1;
        if (!int.TryParse(parts[1], out var m)) return -1;
        return h * 60 + m;
    }

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

    private static (List<SlotCandidate> conflicts, List<SlotCandidate> toCreate) SplitByOverlap(
        IEnumerable<SlotCandidate> candidates,
        Dictionary<string, List<(int startMin, int endMin)>> existingRanges)
    {
        var conflicts = new List<SlotCandidate>();
        var toCreate = new List<SlotCandidate>();
        var newRanges = new Dictionary<string, List<(int startMin, int endMin)>>(StringComparer.OrdinalIgnoreCase);

        foreach (var c in candidates)
        {
            if (!TryParseMinutesRange(c.startTime, c.endTime, out var startMin, out var endMin))
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

        return (conflicts, toCreate);
    }

    private async Task<Dictionary<string, List<(int startMin, int endMin)>>> LoadExistingSlotRangesAsync(
        string leagueId,
        string fieldKey,
        DateOnly from,
        DateOnly to,
        bool includeAvailability)
    {
        var table = await TableClients.GetTableAsync(_svc, Constants.Tables.Slots);
        var prefix = $"SLOT|{leagueId}|";
        var next = prefix + "\uffff";
        var filter = $"PartitionKey ge '{ApiGuards.EscapeOData(prefix)}' and PartitionKey lt '{ApiGuards.EscapeOData(next)}' " +
                     $"and GameDate ge '{from:yyyy-MM-dd}' and GameDate le '{to:yyyy-MM-dd}' " +
                     $"and FieldKey eq '{ApiGuards.EscapeOData(fieldKey)}'";

        var existing = new Dictionary<string, List<(int startMin, int endMin)>>(StringComparer.OrdinalIgnoreCase);
        await foreach (var e in table.QueryAsync<TableEntity>(filter: filter))
        {
            var status = (e.GetString("Status") ?? Constants.Status.SlotOpen).Trim();
            if (string.Equals(status, Constants.Status.SlotCancelled, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!includeAvailability)
            {
                var isAvailability = e.GetBoolean("IsAvailability") ?? false;
                if (isAvailability) continue;
            }

            var gameDate = (e.GetString("GameDate") ?? "").Trim();
            var startTime = (e.GetString("StartTime") ?? "").Trim();
            var endTime = (e.GetString("EndTime") ?? "").Trim();
            if (!TryParseMinutesRange(startTime, endTime, out var startMin, out var endMin)) continue;
            var key = BuildRangeKey(fieldKey, gameDate);
            AddRange(existing, key, startMin, endMin);
        }

        return existing;
    }

    private async Task<int> ClearAvailabilitySlotsAsync(
        TableClient table,
        string leagueId,
        string division,
        string fieldKey,
        DateOnly from,
        DateOnly to)
    {
        var pk = Constants.Pk.Slots(leagueId, division);
        var filter = $"PartitionKey eq '{ApiGuards.EscapeOData(pk)}' " +
                     $"and GameDate ge '{from:yyyy-MM-dd}' and GameDate le '{to:yyyy-MM-dd}' " +
                     $"and FieldKey eq '{ApiGuards.EscapeOData(fieldKey)}'";
        var cleared = 0;
        await foreach (var e in table.QueryAsync<TableEntity>(filter: filter))
        {
            var status = (e.GetString("Status") ?? Constants.Status.SlotOpen).Trim();
            if (!string.Equals(status, Constants.Status.SlotOpen, StringComparison.OrdinalIgnoreCase))
                continue;

            var isAvailability = e.GetBoolean("IsAvailability") ?? false;
            if (!isAvailability) continue;

            await table.DeleteEntityAsync(e.PartitionKey, e.RowKey, e.ETag);
            cleared++;
        }
        return cleared;
    }

    private async Task OverwriteConflictsAsync(
        TableClient table,
        string leagueId,
        string fieldKey,
        List<SlotCandidate> conflicts,
        List<SlotCandidate> created,
        List<SlotCandidate> overwritten,
        List<SlotCandidate> skipped)
    {
        var prefix = $"SLOT|{leagueId}|";
        var next = prefix + "\uffff";
        var byKey = conflicts.ToDictionary(TimeKey, x => x, StringComparer.OrdinalIgnoreCase);
        var dateFrom = conflicts.Min(c => c.gameDate) ?? "";
        var dateTo = conflicts.Max(c => c.gameDate) ?? "";
        var filter = $"PartitionKey ge '{ApiGuards.EscapeOData(prefix)}' and PartitionKey lt '{ApiGuards.EscapeOData(next)}' " +
                     $"and GameDate ge '{ApiGuards.EscapeOData(dateFrom)}' and GameDate le '{ApiGuards.EscapeOData(dateTo)}' " +
                     $"and FieldKey eq '{ApiGuards.EscapeOData(fieldKey)}'";

        await foreach (var e in table.QueryAsync<TableEntity>(filter: filter))
        {
            var status = (e.GetString("Status") ?? Constants.Status.SlotOpen).Trim();
            if (!string.Equals(status, Constants.Status.SlotOpen, StringComparison.OrdinalIgnoreCase))
                continue;

            var isAvailability = e.GetBoolean("IsAvailability") ?? false;
            if (!isAvailability) continue;

            var division = ExtractDivision(e.PartitionKey, leagueId);
            var candidate = new SlotCandidate(
                (e.GetString("GameDate") ?? "").Trim(),
                (e.GetString("StartTime") ?? "").Trim(),
                (e.GetString("EndTime") ?? "").Trim(),
                fieldKey,
                division
            );

            var key = TimeKey(candidate);
            if (!byKey.TryGetValue(key, out var desired)) continue;
            if (!string.Equals(desired.division, candidate.division, StringComparison.OrdinalIgnoreCase)) continue;

            e["OfferingTeamId"] = "AVAILABLE";
            e["HomeTeamId"] = "";
            e["AwayTeamId"] = "";
            e["IsAvailability"] = true;
            e["IsExternalOffer"] = false;
            e["ScheduleRunId"] = "";
            e["ParkName"] = e.GetString("ParkName") ?? "";
            e["FieldName"] = e.GetString("FieldName") ?? "";
            e["DisplayName"] = e.GetString("DisplayName") ?? "";
            e["UpdatedUtc"] = DateTimeOffset.UtcNow;

            await table.UpdateEntityAsync(e, e.ETag, TableUpdateMode.Merge);
            overwritten.Add(candidate);
        }

        var overwriteKeys = new HashSet<string>(overwritten.Select(TimeKey), StringComparer.OrdinalIgnoreCase);
        foreach (var c in conflicts)
        {
            if (!overwriteKeys.Contains(TimeKey(c)))
                skipped.Add(c);
        }
    }

    private static TableEntity BuildSlotEntity(string leagueId, SlotCandidate slot, int gameLengthMinutes, string parkName, string fieldName, string displayName)
    {
        var now = DateTimeOffset.UtcNow;
        var pk = Constants.Pk.Slots(leagueId, slot.division);
        var slotId = Guid.NewGuid().ToString("N");
        return new TableEntity(pk, slotId)
        {
            ["LeagueId"] = leagueId,
            ["SlotId"] = slotId,
            ["Division"] = slot.division,
            ["OfferingTeamId"] = "AVAILABLE",
            ["OfferingEmail"] = "",
            ["HomeTeamId"] = "",
            ["AwayTeamId"] = "",
            ["IsExternalOffer"] = false,
            ["IsAvailability"] = true,
            ["GameDate"] = slot.gameDate,
            ["StartTime"] = slot.startTime,
            ["EndTime"] = slot.endTime,
            ["FieldKey"] = slot.fieldKey,
            ["ParkName"] = parkName,
            ["FieldName"] = fieldName,
            ["DisplayName"] = string.IsNullOrWhiteSpace(displayName) ? $"{parkName} > {fieldName}" : displayName,
            ["GameType"] = "Swap",
            ["Status"] = Constants.Status.SlotOpen,
            ["Notes"] = $"Availability ({gameLengthMinutes} min)",
            ["CreatedUtc"] = now,
            ["UpdatedUtc"] = now
        };
    }

    private static bool TryParseFieldKey(string raw, out string parkCode, out string fieldCode)
    {
        parkCode = "";
        fieldCode = "";
        var v = (raw ?? "").Trim().Trim('/');
        var parts = v.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2) return false;
        parkCode = Slug.Make(parts[0]);
        fieldCode = Slug.Make(parts[1]);
        return !string.IsNullOrWhiteSpace(parkCode) && !string.IsNullOrWhiteSpace(fieldCode);
    }

    private static string ExtractDivision(string pk, string leagueId)
    {
        var prefix = $"SLOT|{leagueId}|";
        if (!pk.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return "";
        return pk[prefix.Length..];
    }

    private record RuleWindowSpec(
        string ruleId,
        DateOnly? startsOn,
        DateOnly? endsOn,
        HashSet<DayOfWeek> days,
        int startMin,
        int endMin);

    private async Task<List<SlotCandidate>> BuildRuleBasedSlotsAsync(
        string leagueId,
        string fieldKey,
        string division,
        DateOnly from,
        DateOnly to,
        int gameLengthMinutes,
        List<(DateOnly start, DateOnly end)> blackouts)
    {
        var rules = await LoadAvailabilityRulesAsync(leagueId, fieldKey, division);
        var exceptionsByRule = await LoadExceptionsByRuleAsync(rules.Select(r => r.ruleId));
        var candidates = new List<SlotCandidate>();
        foreach (var rule in rules)
        {
            if (rule.days.Count == 0) continue;

            var rangeStart = rule.startsOn ?? from;
            var rangeEnd = rule.endsOn ?? to;
            if (rangeEnd < from || rangeStart > to) continue;

            var windowStart = rangeStart > from ? rangeStart : from;
            var windowEnd = rangeEnd < to ? rangeEnd : to;

            var ruleSpec = new AvailabilityRuleSpec(
                rule.ruleId,
                fieldKey,
                division,
                windowStart,
                windowEnd,
                rule.days,
                rule.startMin,
                rule.endMin);

            var expanded = AvailabilityRuleEngine.ExpandRecurringSlots(
                new[] { ruleSpec },
                exceptionsByRule,
                windowStart,
                windowEnd,
                gameLengthMinutes,
                blackouts);

            candidates.AddRange(expanded.Select(s => new SlotCandidate(
                s.GameDate,
                s.StartTime,
                s.EndTime,
                s.FieldKey,
                s.Division)));
        }

        return DeduplicateCandidates(candidates);
    }

    private async Task<List<RuleWindowSpec>> LoadAvailabilityRulesAsync(string leagueId, string fieldKey, string division)
    {
        var table = await TableClients.GetTableAsync(_svc, Constants.Tables.FieldAvailabilityRules);
        var pk = Constants.Pk.FieldAvailabilityRules(leagueId, fieldKey);
        var filter = $"PartitionKey eq '{ApiGuards.EscapeOData(pk)}'";
        var rules = new List<RuleWindowSpec>();
        await foreach (var e in table.QueryAsync<TableEntity>(filter: filter))
        {
            var isActive = e.GetBoolean(Constants.FieldAvailabilityColumns.IsActive) ?? true;
            if (!isActive) continue;
            if (!RuleAppliesToDivision(e, division)) continue;

            var days = ParseRuleDays(e.GetString(Constants.FieldAvailabilityColumns.DaysOfWeek) ?? "");
            if (days.Count == 0) continue;

            var startTime = (e.GetString(Constants.FieldAvailabilityColumns.StartTimeLocal) ?? "").Trim();
            var endTime = (e.GetString(Constants.FieldAvailabilityColumns.EndTimeLocal) ?? "").Trim();
            if (!TimeUtil.IsValidRange(startTime, endTime, out var startMin, out var endMin, out _))
                continue;

            var startsOn = ParseDateOnly(e.GetString(Constants.FieldAvailabilityColumns.StartsOn));
            var endsOn = ParseDateOnly(e.GetString(Constants.FieldAvailabilityColumns.EndsOn));

            rules.Add(new RuleWindowSpec(e.RowKey, startsOn, endsOn, days, startMin, endMin));
        }

        return rules;
    }

    private async Task<Dictionary<string, List<AvailabilityExceptionSpec>>> LoadExceptionsByRuleAsync(IEnumerable<string> ruleIds)
    {
        var table = await TableClients.GetTableAsync(_svc, Constants.Tables.FieldAvailabilityExceptions);
        var results = new Dictionary<string, List<AvailabilityExceptionSpec>>(StringComparer.OrdinalIgnoreCase);

        foreach (var ruleId in ruleIds.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var pk = Constants.Pk.FieldAvailabilityRuleExceptions(ruleId);
            var filter = $"PartitionKey eq '{ApiGuards.EscapeOData(pk)}'";
            var list = new List<AvailabilityExceptionSpec>();

            await foreach (var entity in table.QueryAsync<TableEntity>(filter: filter))
            {
                var dateFromRaw = (entity.GetString(Constants.FieldAvailabilityExceptionColumns.DateFrom) ?? "").Trim();
                var dateToRaw = (entity.GetString(Constants.FieldAvailabilityExceptionColumns.DateTo) ?? "").Trim();
                var startRaw = (entity.GetString(Constants.FieldAvailabilityExceptionColumns.StartTimeLocal) ?? "").Trim();
                var endRaw = (entity.GetString(Constants.FieldAvailabilityExceptionColumns.EndTimeLocal) ?? "").Trim();

                if (!DateOnly.TryParseExact(dateFromRaw, "yyyy-MM-dd", out var dateFrom)) continue;
                if (!DateOnly.TryParseExact(dateToRaw, "yyyy-MM-dd", out var dateTo)) continue;
                if (!TimeUtil.IsValidRange(startRaw, endRaw, out var startMin, out var endMin, out _)) continue;

                list.Add(new AvailabilityExceptionSpec(dateFrom, dateTo, startMin, endMin));
            }

            results[ruleId] = list;
        }

        return results;
    }

    private static DateOnly? ParseDateOnly(string? raw)
    {
        var text = (raw ?? "").Trim();
        if (string.IsNullOrWhiteSpace(text)) return null;
        return DateOnly.TryParseExact(text, "yyyy-MM-dd", out var date) ? date : null;
    }

    private static HashSet<DayOfWeek> ParseRuleDays(string raw)
    {
        var text = (raw ?? "").Trim();
        if (string.IsNullOrWhiteSpace(text)) return new HashSet<DayOfWeek>();

        if (text.StartsWith("["))
        {
            try
            {
                var parsed = System.Text.Json.JsonSerializer.Deserialize<List<string>>(text) ?? new List<string>();
                return NormalizeDays(parsed);
            }
            catch
            {
                return new HashSet<DayOfWeek>();
            }
        }

        var parts = text.Split(new[] { ',', ';', '|', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return NormalizeDays(parts.ToList());
    }

    private static bool RuleAppliesToDivision(TableEntity e, string division)
    {
        var direct = (e.GetString(Constants.FieldAvailabilityColumns.Division) ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(direct))
            return string.Equals(direct, division, StringComparison.OrdinalIgnoreCase);

        var ids = (e.GetString(Constants.FieldAvailabilityColumns.DivisionIds) ?? "").Trim();
        if (string.IsNullOrWhiteSpace(ids)) return true;

        var parts = ids.Split(new[] { ',', ';', '|', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Any(p => string.Equals(p, division, StringComparison.OrdinalIgnoreCase));
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
}
