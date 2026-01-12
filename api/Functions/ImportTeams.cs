using System.Net;
using System.Text;
using Azure;
using Azure.Data.Tables;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using GameSwap.Functions.Storage;
using GameSwap.Functions.Telemetry;

namespace GameSwap.Functions.Functions;

public class ImportTeams
{
    private readonly ILogger _log;
    private readonly TableServiceClient _svc;

    private const string TeamsTableName = Constants.Tables.Teams;

    public ImportTeams(ILoggerFactory lf, TableServiceClient tableServiceClient)
    {
        _log = lf.CreateLogger<ImportTeams>();
        _svc = tableServiceClient;
    }

    [Function("ImportTeams")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "import/teams")] HttpRequestData req)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            await ApiGuards.RequireLeagueAdminAsync(_svc, me.UserId, leagueId);

            var csvText = await CsvUpload.ReadCsvTextAsync(req);
            if (string.IsNullOrWhiteSpace(csvText))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "Empty CSV body.");

            var rows = CsvMini.Parse(csvText);
            if (rows.Count < 2)
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "No CSV rows found.");

            var header = rows[0];
            if (header.Length > 0 && header[0] != null)
                header[0] = header[0].TrimStart('\uFEFF'); // strip BOM

            var idx = CsvMini.HeaderIndex(header);
            var required = new[] { "division", "teamid", "name" };
            var missing = required.Where(x => !idx.ContainsKey(x)).ToList();
            if (missing.Count > 0)
            {
                var headerPreview = string.Join(",", header.Select(x => (x ?? "").Trim()).Take(12));
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST",
                    "Missing required columns. Required: division, teamId, name. Optional: coachName, coachEmail, coachPhone.",
                    new { headerPreview, missing });
            }

            var table = await TableClients.GetTableAsync(_svc, TeamsTableName);

            int upserted = 0, rejected = 0, skipped = 0;
            var errors = new List<object>();
            var actionsByPartition = new Dictionary<string, List<TableTransactionAction>>(StringComparer.OrdinalIgnoreCase);

            for (int i = 1; i < rows.Count; i++)
            {
                var r = rows[i];
                if (CsvMini.IsBlankRow(r)) { skipped++; continue; }

                var division = CsvMini.Get(r, idx, "division").Trim();
                var teamId = CsvMini.Get(r, idx, "teamid").Trim();
                var name = CsvMini.Get(r, idx, "name").Trim();

                var coachName = CsvMini.Get(r, idx, "coachname").Trim();
                var coachEmail = CsvMini.Get(r, idx, "coachemail").Trim();
                var coachPhone = CsvMini.Get(r, idx, "coachphone").Trim();

                if (string.IsNullOrWhiteSpace(division) || string.IsNullOrWhiteSpace(teamId) || string.IsNullOrWhiteSpace(name))
                {
                    rejected++;
                    errors.Add(new { row = i + 1, teamId, error = "division, teamId, name are required." });
                    continue;
                }

                if (ApiGuards.HasInvalidTableKeyChars(division) || ApiGuards.HasInvalidTableKeyChars(teamId))
                {
                    rejected++;
                    errors.Add(new
                    {
                        row = i + 1,
                        teamId,
                        error = $"Invalid division or teamId. Table keys cannot contain: {ApiGuards.InvalidTableKeyCharsMessage}"
                    });
                    continue;
                }

                var pk = $"TEAM|{leagueId}|{division}";
                var rk = teamId;

                var entity = new TableEntity(pk, rk)
                {
                    ["LeagueId"] = leagueId,
                    ["Division"] = division,
                    ["TeamId"] = teamId,
                    ["Name"] = name,
                    ["PrimaryContactName"] = coachName,
                    ["PrimaryContactEmail"] = coachEmail,
                    ["PrimaryContactPhone"] = coachPhone,
                    ["UpdatedUtc"] = DateTimeOffset.UtcNow
                };

                if (!actionsByPartition.TryGetValue(pk, out var actions))
                {
                    actions = new List<TableTransactionAction>(capacity: 100);
                    actionsByPartition[pk] = actions;
                }

                actions.Add(new TableTransactionAction(TableTransactionActionType.UpsertMerge, entity));

                if (actions.Count == 100)
                {
                    var result = await table.SubmitTransactionAsync(actions);
                    upserted += result.Value.Count;
                    actions.Clear();
                }
            }

            foreach (var actions in actionsByPartition.Values)
            {
                if (actions.Count == 0) continue;
                var result = await table.SubmitTransactionAsync(actions);
                upserted += result.Value.Count;
            }

            UsageTelemetry.Track(_log, "api_import_teams", leagueId, me.UserId, new
            {
                upserted,
                rejected,
                skipped
            });

            return ApiResponses.Ok(req, new { leagueId, upserted, rejected, skipped, errors });
        }
        catch (ApiGuards.HttpError ex) { return ApiResponses.FromHttpError(req, ex); }
        catch (RequestFailedException ex)
        {
            var requestId = req.FunctionContext.InvocationId.ToString();
            _log.LogError(ex, "ImportTeams storage request failed. requestId={requestId}", requestId);
            return ApiResponses.Error(
                req,
                HttpStatusCode.BadGateway,
                "STORAGE_ERROR",
                "Storage request failed. This usually means a key contains invalid characters.",
                new { requestId, status = ex.Status, code = ex.ErrorCode });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "ImportTeams failed");
            return ApiResponses.Error(
                req,
                HttpStatusCode.InternalServerError,
                "INTERNAL",
                "Internal Server Error",
                new { exception = ex.GetType().Name, message = ex.Message });
        }
    }
}
