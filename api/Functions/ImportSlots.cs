using System.Net;
using System.Text;
using Azure;
using Azure.Data.Tables;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using GameSwap.Functions.Storage;
using System.Linq;

namespace GameSwap.Functions.Functions;

public class ImportSlots
{
    private readonly ILogger _log;
    private readonly TableServiceClient _svc;

    private const string SlotsTableName = "GameSwapSlots";
    private const string FieldsTableName = "GameSwapFields";

    public ImportSlots(ILoggerFactory lf, TableServiceClient tableServiceClient)
    {
        _log = lf.CreateLogger<ImportSlots>();
        _svc = tableServiceClient;
    }

    [Function("ImportSlots")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "import/slots")] HttpRequestData req)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            await ApiGuards.RequireLeagueAdminAsync(_svc, me.UserId, leagueId);
            if (ApiGuards.HasInvalidTableKeyChars(leagueId))
                return HttpUtil.Json(req, HttpStatusCode.BadRequest, new
                {
                    error = $"Invalid leagueId. Table keys cannot contain: {ApiGuards.InvalidTableKeyCharsMessage}"
                });

            var csvText = await CsvUpload.ReadCsvTextAsync(req);
            if (string.IsNullOrWhiteSpace(csvText))
                return HttpUtil.Json(req, HttpStatusCode.BadRequest, new { error = "Empty CSV body." });

            var rows = CsvMini.Parse(csvText);
            if (rows.Count < 2)
                return HttpUtil.Json(req, HttpStatusCode.BadRequest, new { error = "No CSV rows found." });

            var header = rows[0];
            var idx = CsvMini.HeaderIndex(header);

            // Required normalized columns
            var required = new[]
            {
                "division",
                "offeringteamid",
                "gamedate",
                "starttime",
                "endtime",
                "fieldkey"
            };

            var missing = required.Where(c => !idx.ContainsKey(c)).ToList();
            if (missing.Count > 0)
            {
                return HttpUtil.Json(req, HttpStatusCode.BadRequest, new
                {
                    error = "Missing required columns.",
                    required,
                    missing,
                    optional = new[] { "offeringemail", "gametype", "notes", "status" }
                });
            }

            var slotsTable = await TableClients.GetTableAsync(_svc, SlotsTableName);
            var fieldsTable = await TableClients.GetTableAsync(_svc, FieldsTableName);
            var fieldLookup = await LoadFieldsLookupAsync(fieldsTable, leagueId);

            // PartitionKey batching (Azure Tables transactions are limited to 100 ops and must share the PK)
            var byPk = new Dictionary<string, List<TableTransactionAction>>(StringComparer.OrdinalIgnoreCase);

            int upserted = 0, rejected = 0, skipped = 0;
            var errors = new List<object>();

            for (int i = 1; i < rows.Count; i++)
            {
                var r = rows[i];
                if (CsvMini.IsBlankRow(r)) { skipped++; continue; }

                var division = CsvMini.Get(r, idx, "division").Trim();
                var offeringTeamId = CsvMini.Get(r, idx, "offeringteamid").Trim();
                var offeringEmail = CsvMini.Get(r, idx, "offeringemail").Trim();

                var gameDate = CsvMini.Get(r, idx, "gamedate").Trim();
                var startTime = CsvMini.Get(r, idx, "starttime").Trim();
                var endTime = CsvMini.Get(r, idx, "endtime").Trim();

                var fieldKeyRaw = CsvMini.Get(r, idx, "fieldkey").Trim();

                var gameType = CsvMini.Get(r, idx, "gametype").Trim();
                var notes = CsvMini.Get(r, idx, "notes").Trim();
                var status = CsvMini.Get(r, idx, "status").Trim();

                if (string.IsNullOrWhiteSpace(division) ||
                    string.IsNullOrWhiteSpace(offeringTeamId) ||
                    string.IsNullOrWhiteSpace(gameDate) ||
                    string.IsNullOrWhiteSpace(startTime) ||
                    string.IsNullOrWhiteSpace(endTime) ||
                    string.IsNullOrWhiteSpace(fieldKeyRaw))
                {
                    rejected++;
                    errors.Add(new { row = i + 1, error = "Division, OfferingTeamId, GameDate, StartTime, EndTime, FieldKey are required." });
                    continue;
                }

                if (string.IsNullOrWhiteSpace(gameType)) gameType = "Swap";
                if (string.IsNullOrWhiteSpace(status)) status = "Open";

                if (!TryParseFieldKey(fieldKeyRaw, out var parkCode, out var fieldCode))
                {
                    rejected++;
                    errors.Add(new { row = i + 1, error = "Invalid FieldKey. Use parkCode/fieldCode." });
                    continue;
                }
                if (ApiGuards.HasInvalidTableKeyChars(division))
                {
                    rejected++;
                    errors.Add(new
                    {
                        row = i + 1,
                        error = $"Invalid division. Table keys cannot contain: {ApiGuards.InvalidTableKeyCharsMessage}"
                    });
                    continue;
                }
                if (ApiGuards.HasInvalidTableKeyChars(parkCode) || ApiGuards.HasInvalidTableKeyChars(fieldCode))
                {
                    rejected++;
                    errors.Add(new
                    {
                        row = i + 1,
                        error = $"Invalid fieldKey. Table keys cannot contain: {ApiGuards.InvalidTableKeyCharsMessage}",
                        fieldKey = fieldKeyRaw
                    });
                    continue;
                }

                var fieldKey = $"{parkCode}|{fieldCode}";
                if (!fieldLookup.TryGetValue(fieldKey, out var fieldMeta))
                {
                    rejected++;
                    errors.Add(new { row = i + 1, error = "Field not found in GameSwapFields (import fields first).", fieldKey = fieldKeyRaw });
                    continue;
                }

                if (!fieldMeta.IsActive)
                {
                    rejected++;
                    errors.Add(new { row = i + 1, error = "Field exists but IsActive=false.", fieldKey = fieldKeyRaw });
                    continue;
                }

                var pk = $"SLOT|{leagueId}|{division}";
                var slotId = SafeKey($"{offeringTeamId}|{gameDate}|{startTime}|{endTime}|{parkCode}|{fieldCode}");
                var now = DateTimeOffset.UtcNow;

                var entity = new TableEntity(pk, slotId)
                {
                    ["LeagueId"] = leagueId,
                    ["SlotId"] = slotId,
                    ["Division"] = division,

                    ["OfferingTeamId"] = offeringTeamId,
                    ["HomeTeamId"] = offeringTeamId,
                    ["AwayTeamId"] = "",
                    ["IsExternalOffer"] = false,
                    ["IsAvailability"] = false,
                    ["OfferingEmail"] = offeringEmail,

                    ["GameDate"] = gameDate,
                    ["StartTime"] = startTime,
                    ["EndTime"] = endTime,

                    ["ParkName"] = fieldMeta.ParkName,
                    ["FieldName"] = fieldMeta.FieldName,
                    ["DisplayName"] = fieldMeta.DisplayName,
                    ["FieldKey"] = $"{parkCode}/{fieldCode}",

                    ["GameType"] = gameType,
                    ["Status"] = status,
                    ["Notes"] = notes,

                    ["UpdatedUtc"] = now,
                    ["LastUpdatedUtc"] = now
                };

                if (!byPk.TryGetValue(pk, out var actions))
                {
                    actions = new List<TableTransactionAction>();
                    byPk[pk] = actions;
                }

                actions.Add(new TableTransactionAction(TableTransactionActionType.UpsertMerge, entity));
            }

            foreach (var (pk, actions) in byPk)
            {
                for (int idx2 = 0; idx2 < actions.Count; idx2 += 100)
                {
                    var chunk = actions.Skip(idx2).Take(100).ToList();
                    try
                    {
                        var result = await slotsTable.SubmitTransactionAsync(chunk);
                        upserted += result.Value.Count;
                    }
                    catch (RequestFailedException ex)
                    {
                        _log.LogError(ex, "ImportSlots transaction failed for PK {pk}", pk);
                        errors.Add(new { partitionKey = pk, error = ex.Message });
                    }
                }
            }

            return HttpUtil.Json(req, HttpStatusCode.OK, new
            {
                table = SlotsTableName,
                leagueId,
                upserted,
                rejected,
                skipped,
                errors
            });
        }
        catch (InvalidOperationException inv)
        {
            return HttpUtil.Json(req, HttpStatusCode.BadRequest, new { error = inv.Message });
        }
        catch (UnauthorizedAccessException)
        {
            return HttpUtil.Text(req, HttpStatusCode.Forbidden, "Forbidden");
        }
        catch (RequestFailedException ex)
        {
            var requestId = req.FunctionContext.InvocationId.ToString();
            _log.LogError(ex, "ImportSlots storage request failed. requestId={requestId}", requestId);
            return ApiResponses.Error(
                req,
                HttpStatusCode.BadGateway,
                "STORAGE_ERROR",
                "Storage request failed. This usually means a key contains invalid characters.",
                new { requestId, status = ex.Status, code = ex.ErrorCode });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "ImportSlots failed");
            return ApiResponses.Error(
                req,
                HttpStatusCode.InternalServerError,
                "INTERNAL",
                "Internal Server Error",
                new { exception = ex.GetType().Name, message = ex.Message });
        }
    }

    private sealed record FieldMeta(string ParkName, string FieldName, string DisplayName, bool IsActive);

    private static async Task<Dictionary<string, FieldMeta>> LoadFieldsLookupAsync(TableClient fieldsTable, string leagueId)
    {
        var map = new Dictionary<string, FieldMeta>(StringComparer.OrdinalIgnoreCase);

        // PK = FIELD|{leagueId}|{parkCode}
        var pkPrefix = $"FIELD|{leagueId}|";
        var next = pkPrefix + "\uffff";
        var filter = $"PartitionKey ge '{ApiGuards.EscapeOData(pkPrefix)}' and PartitionKey lt '{ApiGuards.EscapeOData(next)}'";

        await foreach (var e in fieldsTable.QueryAsync<TableEntity>(filter: filter))
        {
            var isActive = e.GetBoolean("IsActive") ?? true;

            var parkName = e.GetString("ParkName") ?? "";
            var fieldName = e.GetString("FieldName") ?? "";
            var displayName = e.GetString("DisplayName") ?? "";

            var parkCode = e.GetString("ParkCode") ?? ExtractParkCodeFromPk(e.PartitionKey, leagueId);
            var fieldCode = e.GetString("FieldCode") ?? e.RowKey;

            if (string.IsNullOrWhiteSpace(displayName) && !string.IsNullOrWhiteSpace(parkName) && !string.IsNullOrWhiteSpace(fieldName))
                displayName = $"{parkName} > {fieldName}";

            if (string.IsNullOrWhiteSpace(parkCode) || string.IsNullOrWhiteSpace(fieldCode))
                continue;

            map[$"{parkCode}|{fieldCode}"] = new FieldMeta(parkName, fieldName, displayName, isActive);
        }

        return map;
    }

    private static string ExtractParkCodeFromPk(string pk, string leagueId)
    {
        var prefix = $"FIELD|{leagueId}|";
        if (!pk.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return "";
        return pk[prefix.Length..];
    }

    private static string SafeKey(string input)
    {
        var bad = new HashSet<char>(new[] { '/', '\\', '#', '?' });
        var sb = new StringBuilder(input.Length);
        foreach (var c in input) sb.Append(bad.Contains(c) ? '_' : c);
        return sb.ToString();
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
}
