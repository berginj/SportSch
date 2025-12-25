using System.Net;
using Azure;
using Azure.Data.Tables;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using GameSwap.Functions.Storage;

namespace GameSwap.Functions.Functions;

public class CancelSlot
{
    private readonly ILogger _log;
    private readonly TableServiceClient _svc;

    private const string SlotsTableName = Constants.Tables.Slots;

    public CancelSlot(ILoggerFactory lf, TableServiceClient tableServiceClient)
    {
        _log = lf.CreateLogger<CancelSlot>();
        _svc = tableServiceClient;
    }

    [Function("CancelSlot")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "slots/{division}/{slotId}/cancel")] HttpRequestData req,
        string division,
        string slotId)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            await ApiGuards.RequireMemberAsync(_svc, me.UserId, leagueId);

            var pk = $"SLOT|{leagueId}|{division}";
            var table = await TableClients.GetTableAsync(_svc, SlotsTableName);
            TableEntity slot;
            try
            {
                slot = (await table.GetEntityAsync<TableEntity>(pk, slotId)).Value;
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return ApiResponses.Error(req, HttpStatusCode.NotFound, "NOT_FOUND", "Slot not found");
            }

            var status = slot.GetString("Status") ?? "Open";
            if (string.Equals(status, "Cancelled", StringComparison.OrdinalIgnoreCase))
                return ApiResponses.Ok(req, new { ok = true, status = Constants.Status.SlotCancelled });

            var offeringTeamId = (slot.GetString("OfferingTeamId") ?? "").Trim();
            var confirmedTeamId = (slot.GetString("ConfirmedTeamId") ?? "").Trim();
            var isGlobalAdmin = await ApiGuards.IsGlobalAdminAsync(_svc, me.UserId);
            var mem = await ApiGuards.GetMembershipAsync(_svc, me.UserId, leagueId);
            var role = ApiGuards.GetRole(mem);
            var (_, myTeamId) = ApiGuards.GetCoachTeam(mem);

            var canCancel = isGlobalAdmin
                || string.Equals(role, Constants.Roles.LeagueAdmin, StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrWhiteSpace(myTeamId)
                    && (
                        string.Equals(myTeamId, offeringTeamId, StringComparison.OrdinalIgnoreCase)
                        || (!string.IsNullOrWhiteSpace(confirmedTeamId) && string.Equals(myTeamId, confirmedTeamId, StringComparison.OrdinalIgnoreCase))
                    ));

            if (!canCancel)
                return ApiResponses.Error(req, HttpStatusCode.Forbidden, "FORBIDDEN", "Forbidden");

            slot["Status"] = Constants.Status.SlotCancelled;
            slot["UpdatedUtc"] = DateTimeOffset.UtcNow;

            await table.UpdateEntityAsync(slot, slot.ETag, TableUpdateMode.Replace);

            return ApiResponses.Ok(req, new { ok = true, status = Constants.Status.SlotCancelled });
        }
        catch (ApiGuards.HttpError ex) { return ApiResponses.FromHttpError(req, ex); }
        catch (Exception ex)
        {
            _log.LogError(ex, "CancelSlot failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
        }
    }
}
