using System.Net;
using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using GameSwap.Functions.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace GameSwap.Functions.Functions;

public class LeagueBackupFunctions
{
    private readonly TableServiceClient _svc;
    private readonly ILogger _log;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public LeagueBackupFunctions(ILoggerFactory lf, TableServiceClient svc)
    {
        _svc = svc;
        _log = lf.CreateLogger<LeagueBackupFunctions>();
    }

    public record BlackoutRange(string? startDate, string? endDate, string? label);
    public record SeasonSnapshot(
        string? springStart,
        string? springEnd,
        string? fallStart,
        string? fallEnd,
        int gameLengthMinutes,
        List<BlackoutRange> blackouts
    );
    public record DivisionSnapshot(
        string code,
        string name,
        bool isActive,
        SeasonSnapshot season
    );
    public record FieldSnapshot(
        string fieldKey,
        string parkName,
        string fieldName,
        string displayName,
        string address,
        string city,
        string state,
        string notes,
        bool isActive,
        List<BlackoutRange> blackouts
    );
    public record LeagueBackupSnapshot(
        string leagueId,
        SeasonSnapshot season,
        List<DivisionSnapshot> divisions,
        List<FieldSnapshot> fields,
        string? notes
    );
    public record BackupSummary(
        string leagueId,
        string savedUtc,
        string savedBy,
        int fieldsCount,
        int divisionsCount,
        SeasonSnapshot season
    );

    private const string SeasonSpringStart = "SeasonSpringStart";
    private const string SeasonSpringEnd = "SeasonSpringEnd";
    private const string SeasonFallStart = "SeasonFallStart";
    private const string SeasonFallEnd = "SeasonFallEnd";
    private const string SeasonGameLengthMinutes = "SeasonGameLengthMinutes";
    private const string SeasonBlackouts = "SeasonBlackouts";

    [Function("GetLeagueBackup")]
    public async Task<HttpResponseData> GetBackup(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "league/backup")] HttpRequestData req)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            await ApiGuards.RequireLeagueAdminAsync(_svc, me.UserId, leagueId);

            var includeSnapshot = string.Equals(ApiGuards.GetQueryParam(req, "includeSnapshot"), "1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(ApiGuards.GetQueryParam(req, "includeSnapshot"), "true", StringComparison.OrdinalIgnoreCase);

            var table = await TableClients.GetTableAsync(_svc, Constants.Tables.LeagueBackups);
            try
            {
                var e = (await table.GetEntityAsync<TableEntity>(Constants.Pk.LeagueBackups, leagueId)).Value;
                var summary = new BackupSummary(
                    leagueId: leagueId,
                    savedUtc: (e.GetString("SavedUtc") ?? "").Trim(),
                    savedBy: (e.GetString("SavedBy") ?? "").Trim(),
                    fieldsCount: e.GetInt32("FieldsCount") ?? 0,
                    divisionsCount: e.GetInt32("DivisionsCount") ?? 0,
                    season: new SeasonSnapshot(
                        springStart: (e.GetString(SeasonSpringStart) ?? "").Trim(),
                        springEnd: (e.GetString(SeasonSpringEnd) ?? "").Trim(),
                        fallStart: (e.GetString(SeasonFallStart) ?? "").Trim(),
                        fallEnd: (e.GetString(SeasonFallEnd) ?? "").Trim(),
                        gameLengthMinutes: e.GetInt32(SeasonGameLengthMinutes) ?? 0,
                        blackouts: ParseBlackouts(e.GetString(SeasonBlackouts))
                    )
                );
                if (!includeSnapshot)
                    return ApiResponses.Ok(req, new { exists = true, backup = summary });

                var json = (e.GetString("SnapshotJson") ?? "").Trim();
                LeagueBackupSnapshot? snapshot = null;
                if (!string.IsNullOrWhiteSpace(json))
                {
                    try
                    {
                        snapshot = JsonSerializer.Deserialize<LeagueBackupSnapshot>(json, JsonOptions);
                    }
                    catch
                    {
                        snapshot = null;
                    }
                }

                return ApiResponses.Ok(req, new { exists = true, backup = summary, snapshot });
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return ApiResponses.Ok(req, new { exists = false, backup = (BackupSummary?)null });
            }
        }
        catch (ApiGuards.HttpError ex) { return ApiResponses.FromHttpError(req, ex); }
        catch (Exception ex)
        {
            _log.LogError(ex, "GetLeagueBackup failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
        }
    }

    [Function("SaveLeagueBackup")]
    public async Task<HttpResponseData> SaveBackup(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "league/backup")] HttpRequestData req)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            await ApiGuards.RequireLeagueAdminAsync(_svc, me.UserId, leagueId);

            var snapshot = await BuildSnapshotAsync(leagueId);
            var json = JsonSerializer.Serialize(snapshot, JsonOptions);
            var now = DateTimeOffset.UtcNow;
            var savedBy = (me.Email ?? me.UserId ?? "").Trim();

            var entity = new TableEntity(Constants.Pk.LeagueBackups, leagueId)
            {
                ["LeagueId"] = leagueId,
                ["SnapshotJson"] = json,
                ["SnapshotVersion"] = 1,
                ["SavedUtc"] = now.ToString("O"),
                ["SavedBy"] = savedBy,
                ["FieldsCount"] = snapshot.fields.Count,
                ["DivisionsCount"] = snapshot.divisions.Count,
                [SeasonSpringStart] = (snapshot.season.springStart ?? "").Trim(),
                [SeasonSpringEnd] = (snapshot.season.springEnd ?? "").Trim(),
                [SeasonFallStart] = (snapshot.season.fallStart ?? "").Trim(),
                [SeasonFallEnd] = (snapshot.season.fallEnd ?? "").Trim(),
                [SeasonGameLengthMinutes] = snapshot.season.gameLengthMinutes,
                [SeasonBlackouts] = JsonSerializer.Serialize(snapshot.season.blackouts ?? new List<BlackoutRange>(), JsonOptions),
                ["UpdatedUtc"] = now
            };

            var table = await TableClients.GetTableAsync(_svc, Constants.Tables.LeagueBackups);
            await table.UpsertEntityAsync(entity, TableUpdateMode.Replace);

            var summary = new BackupSummary(
                leagueId: leagueId,
                savedUtc: entity.GetString("SavedUtc") ?? "",
                savedBy: savedBy,
                fieldsCount: snapshot.fields.Count,
                divisionsCount: snapshot.divisions.Count,
                season: snapshot.season
            );

            return ApiResponses.Ok(req, new { exists = true, backup = summary });
        }
        catch (ApiGuards.HttpError ex) { return ApiResponses.FromHttpError(req, ex); }
        catch (Exception ex)
        {
            _log.LogError(ex, "SaveLeagueBackup failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
        }
    }

    [Function("RestoreLeagueBackup")]
    public async Task<HttpResponseData> RestoreBackup(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "league/backup/restore")] HttpRequestData req)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            await ApiGuards.RequireLeagueAdminAsync(_svc, me.UserId, leagueId);

            var table = await TableClients.GetTableAsync(_svc, Constants.Tables.LeagueBackups);
            TableEntity entity;
            try
            {
                entity = (await table.GetEntityAsync<TableEntity>(Constants.Pk.LeagueBackups, leagueId)).Value;
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return ApiResponses.Error(req, HttpStatusCode.NotFound, "NOT_FOUND", "Backup not found.");
            }

            var json = (entity.GetString("SnapshotJson") ?? "").Trim();
            if (string.IsNullOrWhiteSpace(json))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "Backup snapshot is empty.");

            LeagueBackupSnapshot snapshot;
            try
            {
                snapshot = JsonSerializer.Deserialize<LeagueBackupSnapshot>(json, JsonOptions)
                    ?? throw new Exception("Snapshot missing.");
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "RestoreLeagueBackup failed to parse snapshot for {leagueId}", leagueId);
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "Backup snapshot could not be parsed.");
            }

            await RestoreLeagueSeasonAsync(leagueId, snapshot.season);
            var divisionsRestored = await RestoreDivisionsAsync(leagueId, snapshot.divisions);
            var fieldsRestored = await RestoreFieldsAsync(leagueId, snapshot.fields);

            return ApiResponses.Ok(req, new
            {
                restored = true,
                fieldsRestored,
                divisionsRestored
            });
        }
        catch (ApiGuards.HttpError ex) { return ApiResponses.FromHttpError(req, ex); }
        catch (Exception ex)
        {
            _log.LogError(ex, "RestoreLeagueBackup failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
        }
    }

    private async Task<LeagueBackupSnapshot> BuildSnapshotAsync(string leagueId)
    {
        var fieldsTask = LoadFieldsAsync(leagueId);
        var divisionsTask = LoadDivisionsAsync(leagueId);
        var seasonTask = LoadLeagueSeasonAsync(leagueId);
        await Task.WhenAll(fieldsTask, divisionsTask, seasonTask);

        return new LeagueBackupSnapshot(
            leagueId: leagueId,
            season: seasonTask.Result,
            divisions: divisionsTask.Result,
            fields: fieldsTask.Result,
            notes: null
        );
    }

    private async Task<SeasonSnapshot> LoadLeagueSeasonAsync(string leagueId)
    {
        var leagues = await TableClients.GetTableAsync(_svc, Constants.Tables.Leagues);
        var e = (await leagues.GetEntityAsync<TableEntity>(Constants.Pk.Leagues, leagueId)).Value;
        return new SeasonSnapshot(
            springStart: (e.GetString("SpringStart") ?? "").Trim(),
            springEnd: (e.GetString("SpringEnd") ?? "").Trim(),
            fallStart: (e.GetString("FallStart") ?? "").Trim(),
            fallEnd: (e.GetString("FallEnd") ?? "").Trim(),
            gameLengthMinutes: e.GetInt32("GameLengthMinutes") ?? 0,
            blackouts: ParseBlackouts(e.GetString("Blackouts"))
        );
    }

    private async Task<List<DivisionSnapshot>> LoadDivisionsAsync(string leagueId)
    {
        var table = await TableClients.GetTableAsync(_svc, Constants.Tables.Divisions);
        var list = new List<DivisionSnapshot>();
        var pk = Constants.Pk.Divisions(leagueId);
        await foreach (var e in table.QueryAsync<TableEntity>(x => x.PartitionKey == pk))
        {
            list.Add(new DivisionSnapshot(
                code: e.RowKey,
                name: (e.GetString("Name") ?? "").Trim(),
                isActive: e.GetBoolean("IsActive") ?? true,
                season: new SeasonSnapshot(
                    springStart: (e.GetString(SeasonSpringStart) ?? "").Trim(),
                    springEnd: (e.GetString(SeasonSpringEnd) ?? "").Trim(),
                    fallStart: (e.GetString(SeasonFallStart) ?? "").Trim(),
                    fallEnd: (e.GetString(SeasonFallEnd) ?? "").Trim(),
                    gameLengthMinutes: e.GetInt32(SeasonGameLengthMinutes) ?? 0,
                    blackouts: ParseBlackouts(e.GetString(SeasonBlackouts))
                )
            ));
        }

        return list.OrderBy(x => x.code).ToList();
    }

    private async Task<List<FieldSnapshot>> LoadFieldsAsync(string leagueId)
    {
        var table = await TableClients.GetTableAsync(_svc, Constants.Tables.Fields);
        var list = new List<FieldSnapshot>();
        var pkPrefix = $"FIELD|{leagueId}|";
        var next = pkPrefix + "\uffff";
        var filter = $"PartitionKey ge '{ApiGuards.EscapeOData(pkPrefix)}' and PartitionKey lt '{ApiGuards.EscapeOData(next)}'";

        await foreach (var e in table.QueryAsync<TableEntity>(filter: filter))
        {
            var parkCode = ExtractParkCodeFromPk(e.PartitionKey, leagueId);
            var fieldCode = e.RowKey;
            if (string.IsNullOrWhiteSpace(parkCode) || string.IsNullOrWhiteSpace(fieldCode)) continue;

            list.Add(new FieldSnapshot(
                fieldKey: $"{parkCode}/{fieldCode}",
                parkName: (e.GetString("ParkName") ?? "").Trim(),
                fieldName: (e.GetString("FieldName") ?? "").Trim(),
                displayName: (e.GetString("DisplayName") ?? "").Trim(),
                address: (e.GetString("Address") ?? "").Trim(),
                city: (e.GetString("City") ?? "").Trim(),
                state: (e.GetString("State") ?? "").Trim(),
                notes: (e.GetString("Notes") ?? "").Trim(),
                isActive: e.GetBoolean("IsActive") ?? true,
                blackouts: ParseBlackouts(e.GetString("Blackouts"))
            ));
        }

        return list.OrderBy(x => x.displayName).ToList();
    }

    private async Task RestoreLeagueSeasonAsync(string leagueId, SeasonSnapshot season)
    {
        var leagues = await TableClients.GetTableAsync(_svc, Constants.Tables.Leagues);
        var entity = (await leagues.GetEntityAsync<TableEntity>(Constants.Pk.Leagues, leagueId)).Value;
        entity["SpringStart"] = (season.springStart ?? "").Trim();
        entity["SpringEnd"] = (season.springEnd ?? "").Trim();
        entity["FallStart"] = (season.fallStart ?? "").Trim();
        entity["FallEnd"] = (season.fallEnd ?? "").Trim();
        entity["GameLengthMinutes"] = season.gameLengthMinutes;
        entity["Blackouts"] = JsonSerializer.Serialize(season.blackouts ?? new List<BlackoutRange>(), JsonOptions);
        entity["UpdatedUtc"] = DateTimeOffset.UtcNow;
        await leagues.UpdateEntityAsync(entity, entity.ETag, TableUpdateMode.Replace);
    }

    private async Task<int> RestoreDivisionsAsync(string leagueId, List<DivisionSnapshot> divisions)
    {
        var table = await TableClients.GetTableAsync(_svc, Constants.Tables.Divisions);
        var pk = Constants.Pk.Divisions(leagueId);
        await DeleteByFilterAsync(table, $"PartitionKey eq '{ApiGuards.EscapeOData(pk)}'");

        var now = DateTimeOffset.UtcNow;
        var restored = 0;
        foreach (var division in divisions)
        {
            var code = (division.code ?? "").Trim();
            if (string.IsNullOrWhiteSpace(code)) continue;
            ApiGuards.EnsureValidTableKeyPart("division code", code);

            var entity = new TableEntity(pk, code)
            {
                ["LeagueId"] = leagueId,
                ["Code"] = code,
                ["Name"] = (division.name ?? "").Trim(),
                ["IsActive"] = division.isActive,
                [SeasonSpringStart] = (division.season.springStart ?? "").Trim(),
                [SeasonSpringEnd] = (division.season.springEnd ?? "").Trim(),
                [SeasonFallStart] = (division.season.fallStart ?? "").Trim(),
                [SeasonFallEnd] = (division.season.fallEnd ?? "").Trim(),
                [SeasonGameLengthMinutes] = division.season.gameLengthMinutes,
                [SeasonBlackouts] = JsonSerializer.Serialize(division.season.blackouts ?? new List<BlackoutRange>(), JsonOptions),
                ["UpdatedUtc"] = now
            };

            await table.UpsertEntityAsync(entity, TableUpdateMode.Replace);
            restored++;
        }

        return restored;
    }

    private async Task<int> RestoreFieldsAsync(string leagueId, List<FieldSnapshot> fields)
    {
        var table = await TableClients.GetTableAsync(_svc, Constants.Tables.Fields);
        var pkPrefix = $"FIELD|{leagueId}|";
        await DeleteByFilterAsync(table, PrefixFilter(pkPrefix));

        var now = DateTimeOffset.UtcNow;
        var restored = 0;
        foreach (var field in fields)
        {
            var parts = (field.fieldKey ?? "").Split('/', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2) continue;
            var parkCode = parts[0].Trim();
            var fieldCode = parts[1].Trim();
            if (string.IsNullOrWhiteSpace(parkCode) || string.IsNullOrWhiteSpace(fieldCode)) continue;
            ApiGuards.EnsureValidTableKeyPart("parkCode", parkCode);
            ApiGuards.EnsureValidTableKeyPart("fieldCode", fieldCode);

            var entity = new TableEntity(Constants.Pk.Fields(leagueId, parkCode), fieldCode)
            {
                ["ParkName"] = (field.parkName ?? "").Trim(),
                ["FieldName"] = (field.fieldName ?? "").Trim(),
                ["DisplayName"] = (field.displayName ?? "").Trim(),
                ["Address"] = (field.address ?? "").Trim(),
                ["City"] = (field.city ?? "").Trim(),
                ["State"] = (field.state ?? "").Trim(),
                ["Notes"] = (field.notes ?? "").Trim(),
                ["IsActive"] = field.isActive,
                ["Blackouts"] = JsonSerializer.Serialize(field.blackouts ?? new List<BlackoutRange>(), JsonOptions),
                ["UpdatedUtc"] = now
            };

            await table.UpsertEntityAsync(entity, TableUpdateMode.Replace);
            restored++;
        }

        return restored;
    }

    private static string PrefixFilter(string prefix)
        => $"PartitionKey ge '{ApiGuards.EscapeOData(prefix)}' and PartitionKey lt '{ApiGuards.EscapeOData(prefix + "~")}'";

    private async Task<int> DeleteByFilterAsync(TableClient table, string filter)
    {
        var deleted = 0;
        await foreach (var e in table.QueryAsync<TableEntity>(filter: filter))
        {
            await table.DeleteEntityAsync(e.PartitionKey, e.RowKey, ETag.All);
            deleted++;
        }

        return deleted;
    }

    private static List<BlackoutRange> ParseBlackouts(string? raw)
    {
        var blackoutsRaw = (raw ?? "").Trim();
        if (string.IsNullOrWhiteSpace(blackoutsRaw)) return new List<BlackoutRange>();
        try
        {
            return JsonSerializer.Deserialize<List<BlackoutRange>>(blackoutsRaw, JsonOptions) ?? new List<BlackoutRange>();
        }
        catch
        {
            return new List<BlackoutRange>();
        }
    }

    private static string ExtractParkCodeFromPk(string pk, string leagueId)
    {
        var prefix = $"FIELD|{leagueId}|";
        if (!pk.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return "";
        return pk[prefix.Length..];
    }
}
