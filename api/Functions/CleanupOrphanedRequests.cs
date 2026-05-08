using Azure.Data.Tables;
using GameSwap.Functions.Repositories;
using GameSwap.Functions.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace GameSwap.Functions.Functions;

/// <summary>
/// Background job to clean up orphaned pending requests for confirmed slots.
/// Runs daily to maintain data cleanliness.
///
/// Orphaned requests can occur when:
/// - Slot is confirmed by one team
/// - Best-effort denial of other pending requests fails (ETag mismatch, network issue)
/// - Result: Pending requests exist for a slot that's already Confirmed
///
/// This is cosmetic cleanup - UI uses slot status as source of truth.
/// </summary>
public class CleanupOrphanedRequests
{
    private readonly TableServiceClient _tableService;
    private readonly ISlotRepository _slotRepo;
    private readonly IRequestRepository _requestRepo;
    private readonly ILogger _log;

    public CleanupOrphanedRequests(
        TableServiceClient tableService,
        ISlotRepository slotRepo,
        IRequestRepository requestRepo,
        ILoggerFactory loggerFactory)
    {
        _tableService = tableService;
        _slotRepo = slotRepo;
        _requestRepo = requestRepo;
        _log = loggerFactory.CreateLogger<CleanupOrphanedRequests>();
    }

    /// <summary>
    /// Timer trigger: Runs daily at 2:00 AM UTC
    /// NCRONTAB format: "0 0 2 * * *" = second minute hour day month dayOfWeek
    /// </summary>
    [Function("CleanupOrphanedRequests")]
    public async Task Run([TimerTrigger("0 0 2 * * *")] TimerInfo timer)
    {
        _log.LogInformation("Starting orphaned request cleanup job at {Time}", DateTimeOffset.UtcNow);

        var cleanedCount = 0;
        var checkedSlots = 0;
        var checkedRequests = 0;
        var errors = 0;

        try
        {
            // Query all confirmed slots across all leagues/divisions
            // We'll process them in batches to avoid memory issues
            var slotFilter = new SlotQueryFilter
            {
                LeagueId = "", // Will iterate leagues
                Division = null,
                Status = Constants.Status.SlotConfirmed,
                PageSize = 100
            };

            // For MVP: Query specific leagues (in production, iterate all leagues)
            // In future, could get league list from Leagues table
            var leaguesToCheck = await GetLeaguesToCheckAsync();

            foreach (var leagueId in leaguesToCheck)
            {
                slotFilter.LeagueId = leagueId;
                string? continuationToken = null;

                do
                {
                    var slotsResult = await _slotRepo.QuerySlotsAsync(slotFilter, continuationToken);
                    checkedSlots += slotsResult.Items.Count;

                    foreach (var slot in slotsResult.Items)
                    {
                        var slotId = slot.RowKey;
                        var division = slot.GetString("Division") ?? "";
                        if (string.IsNullOrWhiteSpace(division)) continue;

                        try
                        {
                            // Get pending requests for this confirmed slot
                            var pendingRequests = await _requestRepo.GetPendingRequestsForSlotAsync(
                                leagueId, division, slotId);

                            checkedRequests += pendingRequests.Count;

                            foreach (var request in pendingRequests)
                            {
                                try
                                {
                                    // Mark as denied with cleanup reason
                                    request["Status"] = Constants.Status.SlotRequestDenied;
                                    request["RejectedUtc"] = DateTimeOffset.UtcNow;
                                    request["UpdatedUtc"] = DateTimeOffset.UtcNow;
                                    request["RejectionReason"] = "Slot was confirmed to another team (cleanup job)";

                                    await _requestRepo.UpdateRequestAsync(request, request.ETag);
                                    cleanedCount++;

                                    _log.LogInformation(
                                        "Cleaned orphaned request {RequestId} for confirmed slot {SlotId} in {LeagueId}/{Division}",
                                        request.RowKey, slotId, leagueId, division);
                                }
                                catch (Exception ex)
                                {
                                    _log.LogWarning(ex,
                                        "Failed to clean orphaned request {RequestId} for slot {SlotId}",
                                        request.RowKey, slotId);
                                    errors++;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _log.LogWarning(ex,
                                "Failed to process slot {SlotId} in cleanup job", slotId);
                            errors++;
                        }
                    }

                    continuationToken = slotsResult.ContinuationToken;

                } while (!string.IsNullOrWhiteSpace(continuationToken));
            }

            _log.LogInformation(
                "Orphaned request cleanup completed. Checked {CheckedSlots} slots, {CheckedRequests} requests, cleaned {CleanedCount}, errors {Errors}",
                checkedSlots, checkedRequests, cleanedCount, errors);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Orphaned request cleanup job failed");
        }
    }

    private async Task<IEnumerable<string>> GetLeaguesToCheckAsync()
    {
        var leaguesTable = await TableClients.GetTableAsync(_tableService, Constants.Tables.Leagues);

        var leagueIds = new List<string>();
        await foreach (var league in leaguesTable.QueryAsync<TableEntity>(filter: ""))
        {
            var leagueId = (league.RowKey ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(leagueId))
                leagueIds.Add(leagueId);
        }

        _log.LogInformation("Cleanup job found {Count} leagues to process", leagueIds.Count);
        return leagueIds;
    }
}
