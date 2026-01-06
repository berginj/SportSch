using System.Net;
using Azure;
using Azure.Data.Tables;
using GameSwap.Functions.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace GameSwap.Functions.Functions;

public class GetMe
{
    private readonly TableServiceClient _svc;
    private readonly ILogger _log;

    public GetMe(ILoggerFactory lf, TableServiceClient svc)
    {
        _svc = svc;
        _log = lf.CreateLogger<GetMe>();
    }

    [Function("GetMe")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "me")] HttpRequestData req)
    {
        try
        {
            var me = IdentityUtil.GetMe(req);

            // Contract-compliant: unauthenticated must be a 401, not "UNKNOWN"
            if (string.IsNullOrWhiteSpace(me.UserId) || me.UserId == "UNKNOWN")
            {
                return ApiResponses.Error(req, HttpStatusCode.Unauthorized,
                    "UNAUTHENTICATED", "You must be signed in.");
            }

            var isGlobalAdmin = await ApiGuards.IsGlobalAdminAsync(_svc, me.UserId);

            var memberships = new List<object>();

            // Memberships table: PK = userId, RK = leagueId
            var memTable = await TableClients.GetTableAsync(_svc, Constants.Tables.Memberships);
            var filter = $"PartitionKey eq '{ApiGuards.EscapeOData(me.UserId)}'";

            await foreach (var e in memTable.QueryAsync<TableEntity>(filter: filter))
            {
                var role = (e.GetString("Role") ?? "").Trim();
                var division = (e.GetString("Division") ?? "").Trim();
                var teamId = (e.GetString("TeamId") ?? "").Trim();

                // Coach includes team assignment if present
                if (string.Equals(role, Constants.Roles.Coach, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(division)
                    && !string.IsNullOrWhiteSpace(teamId))
                {
                    memberships.Add(new
                    {
                        leagueId = e.RowKey,
                        role,
                        team = new { division, teamId }
                    });
                }
                else
                {
                    memberships.Add(new
                    {
                        leagueId = e.RowKey,
                        role
                    });
                }
            }

            string homeLeagueId = "";
            try
            {
                var usersTable = await TableClients.GetTableAsync(_svc, Constants.Tables.Users);
                var user = (await usersTable.GetEntityAsync<TableEntity>(Constants.Pk.Users, me.UserId)).Value;
                homeLeagueId = (user.GetString("HomeLeagueId") ?? "").Trim();
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                homeLeagueId = "";
            }

            return ApiResponses.Ok(req, new
            {
                userId = me.UserId,
                email = me.Email,
                isGlobalAdmin,
                memberships,
                homeLeagueId
            });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "GetMe failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
        }
    }
}
