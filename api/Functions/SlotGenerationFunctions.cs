using System.Globalization;
using System.Linq;
using System.Net;
using Azure;
using Azure.Data.Tables;
using GameSwap.Functions.Storage;
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

            var league = await GetLeagueAsync(leagueId);
            var gameLengthMinutes = league.gameLengthMinutes;
            if (gameLengthMinutes <= 0)
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "League game length must be set by global admin.");

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

            var blackoutRanges = league.blackouts;
            var candidateSlots = useRules
                ? await BuildRuleBasedSlotsAsync(leagueId, fieldKey, division, from, to, gameLengthMinutes, blackoutRanges)
                : BuildCandidateSlots(from, to, days, startMin, endMin, gameLengthMinutes, fieldKey, division, blackoutRanges);

            var existing = await LoadExistingSlotsAsync(leagueId, fieldKey, from, to);
            var conflicts = candidateSlots.Where(s => existing.Contains(TimeKey(s))).ToList();
            var toCreate = candidateSlots.Where(s => !existing.Contains(TimeKey(s))).ToList();

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
                existing = await LoadExistingSlotsAsync(leagueId, fieldKey, from, to, includeAvailability: false);
                conflicts = candidateSlots.Where(s => existing.Contains(TimeKey(s))).ToList();
                toCreate = candidateSlots.Where(s => !existing.Contains(TimeKey(s))).ToList();
            }
            foreach (var slot in toCreate)
            {
                var entity = BuildSlotEntity(leagueId, slot, gameLengthMinutes, parkName, fieldName, displayName);
                await slotsTable.AddEntityAsync(entity);
                created.Add(slot);
            }

            if (applyMode == "overwrite" && conflicts.Count > 0)
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

    private record LeagueSeasonConfig(int gameLengthMinutes, List<(DateOnly start, DateOnly end)> blackouts);
    private record BlackoutRange(string? startDate, string? endDate, string? label);

    private async Task<LeagueSeasonConfig> GetLeagueAsync(string leagueId)
    {
        var table = await TableClients.GetTableAsync(_svc, Constants.Tables.Leagues);
        var e = (await table.GetEntityAsync<TableEntity>(Constants.Pk.Leagues, leagueId)).Value;
        var gameLengthMinutes = e.GetInt32("GameLengthMinutes") ?? 0;

        var blackouts = new List<(DateOnly start, DateOnly end)>();
        var blackoutsRaw = (e.GetString("Blackouts") ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(blackoutsRaw))
        {
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
                blackouts = new List<(DateOnly start, DateOnly end)>();
            }
        }

        return new LeagueSeasonConfig(gameLengthMinutes, blackouts);
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

    private async Task<HashSet<string>> LoadExistingSlotsAsync(string leagueId, string fieldKey, DateOnly from, DateOnly to)
        => await LoadExistingSlotsAsync(leagueId, fieldKey, from, to, includeAvailability: true);

    private async Task<HashSet<string>> LoadExistingSlotsAsync(
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

        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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

            var division = ExtractDivision(e.PartitionKey, leagueId);
            var candidate = new SlotCandidate(
                (e.GetString("GameDate") ?? "").Trim(),
                (e.GetString("StartTime") ?? "").Trim(),
                (e.GetString("EndTime") ?? "").Trim(),
                fieldKey,
                division
            );

            existing.Add(TimeKey(candidate));
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
        var dateFrom = conflicts.Min(c => c.gameDate);
        var dateTo = conflicts.Max(c => c.gameDate);
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

    private record AvailabilityRuleSpec(
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
        var candidates = new List<SlotCandidate>();
        foreach (var rule in rules)
        {
            if (rule.days.Count == 0) continue;

            var rangeStart = rule.startsOn ?? from;
            var rangeEnd = rule.endsOn ?? to;
            if (rangeEnd < from || rangeStart > to) continue;

            var windowStart = rangeStart > from ? rangeStart : from;
            var windowEnd = rangeEnd < to ? rangeEnd : to;

            candidates.AddRange(BuildCandidateSlots(
                windowStart,
                windowEnd,
                rule.days,
                rule.startMin,
                rule.endMin,
                gameLengthMinutes,
                fieldKey,
                division,
                blackouts));
        }

        return DeduplicateCandidates(candidates);
    }

    private async Task<List<AvailabilityRuleSpec>> LoadAvailabilityRulesAsync(string leagueId, string fieldKey, string division)
    {
        var table = await TableClients.GetTableAsync(_svc, Constants.Tables.FieldAvailabilityRules);
        var pk = Constants.Pk.FieldAvailabilityRules(leagueId, fieldKey);
        var filter = $"PartitionKey eq '{ApiGuards.EscapeOData(pk)}'";
        var rules = new List<AvailabilityRuleSpec>();
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

            rules.Add(new AvailabilityRuleSpec(startsOn, endsOn, days, startMin, endMin));
        }

        return rules;
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
