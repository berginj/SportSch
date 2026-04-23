using System.Net;
using Azure.Data.Tables;
using GameSwap.Functions.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace GameSwap.Functions.Functions;

/// <summary>
/// Endpoint for monitoring notification delivery metrics.
/// Helps track notification system health and delivery rates.
/// </summary>
public class GetNotificationMetrics
{
    private readonly TableServiceClient _tableService;
    private readonly ILogger _log;

    public GetNotificationMetrics(TableServiceClient tableService, ILoggerFactory loggerFactory)
    {
        _tableService = tableService;
        _log = loggerFactory.CreateLogger<GetNotificationMetrics>();
    }

    [Function("GetNotificationMetrics")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "notifications/metrics")]
        HttpRequestData req)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);

            // Authorization: Admin only
            await ApiGuards.RequireLeagueAdminAsync(_tableService, me.UserId, leagueId);

            var days = int.TryParse(ApiGuards.GetQueryParam(req, "days"), out var d) ? d : 7;
            if (days < 1) days = 7;
            if (days > 90) days = 90;

            var cutoffDate = DateTime.UtcNow.AddDays(-days);

            // Query notifications for this league in the time window
            var notificationsTable = await TableClients.GetTableAsync(_tableService, Constants.Tables.Notifications);

            var totalSent = 0;
            var totalFailed = 0;
            var totalPending = 0;
            var failuresByReason = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            // Note: This queries across all users - in production with many users,
            // consider aggregating metrics in a separate table or using Azure Monitor
            await foreach (var entity in notificationsTable.QueryAsync<TableEntity>(
                filter: $"LeagueId eq '{ApiGuards.EscapeOData(leagueId)}'"))
            {
                var createdUtc = entity.GetDateTime("CreatedUtc");
                if (createdUtc < cutoffDate) continue;

                var deliveryStatus = (entity.GetString("DeliveryStatus") ?? "Sent").Trim();

                switch (deliveryStatus.ToLower())
                {
                    case "sent":
                        totalSent++;
                        break;
                    case "failed":
                        totalFailed++;
                        var reason = (entity.GetString("FailureReason") ?? "Unknown").Trim();
                        if (failuresByReason.ContainsKey(reason))
                            failuresByReason[reason]++;
                        else
                            failuresByReason[reason] = 1;
                        break;
                    case "pending":
                        totalPending++;
                        break;
                }
            }

            var total = totalSent + totalFailed + totalPending;
            var deliveryRate = total > 0 ? (double)totalSent / total : 1.0;

            var metrics = new
            {
                days,
                period = new
                {
                    from = cutoffDate,
                    to = DateTime.UtcNow
                },
                total,
                totalSent,
                totalFailed,
                totalPending,
                deliveryRate = Math.Round(deliveryRate, 4),
                deliveryPercentage = Math.Round(deliveryRate * 100, 2),
                failuresByReason
            };

            return ApiResponses.Ok(req, metrics);
        }
        catch (ApiGuards.HttpError ex)
        {
            return ApiResponses.FromHttpError(req, ex);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "GetNotificationMetrics failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, ErrorCodes.INTERNAL_ERROR, "Internal Server Error");
        }
    }
}
