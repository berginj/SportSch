using Azure.Data.Tables;
using Microsoft.Extensions.Hosting;

namespace GameSwap.Functions.Storage;

public sealed class TableStartup : IHostedService
{
    private readonly TableServiceClient _serviceClient;

    public TableStartup(TableServiceClient serviceClient)
    {
        _serviceClient = serviceClient;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var tableNames = new[]
        {
            Constants.Tables.Leagues,
            Constants.Tables.Memberships,
            Constants.Tables.GlobalAdmins,
            Constants.Tables.Users,
            Constants.Tables.AccessRequests,
            Constants.Tables.Fields,
            Constants.Tables.Divisions,
            Constants.Tables.Events,
            Constants.Tables.Slots,
            Constants.Tables.SlotRequests,
            Constants.Tables.ScheduleRuns,
            Constants.Tables.FieldAvailabilityRules,
            Constants.Tables.FieldAvailabilityExceptions,
            Constants.Tables.FieldAvailabilityAllocations,
            Constants.Tables.Teams,
            Constants.Tables.TeamContacts,
            Constants.Tables.Seasons,
            Constants.Tables.SeasonDivisions,
            Constants.Tables.LeagueInvites,
            Constants.Tables.LeagueBackups
        };

        foreach (var tableName in tableNames)
        {
            var client = _serviceClient.GetTableClient(tableName);
            await client.CreateIfNotExistsAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
