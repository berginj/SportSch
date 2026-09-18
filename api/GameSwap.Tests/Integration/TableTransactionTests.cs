using System;
using System.Threading.Tasks;
using Azure;
using Azure.Data.Tables;
using GameSwap.Functions.Repositories;
using GameSwap.Functions.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameSwap.Tests.Integration;

public sealed class StorageFactAttribute : FactAttribute
{
    public StorageFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("GAMESWAP_STORAGE_TESTS") != "1")
            Skip = "Start local Azurite and set GAMESWAP_STORAGE_TESTS=1. Release CI runs these tests.";
    }
}

public class TableTransactionTests
{
    [StorageFact]
    public async Task CompetingUpdateRejectsEntireRescheduleTransaction()
    {
        // Deliberately fixed to the emulator: never read a production connection string.
        var service = new TableServiceClient("UseDevelopmentStorage=true");
        var table = service.GetTableClient(Constants.Tables.Slots);
        await table.CreateIfNotExistsAsync();
        var league = "test-" + Guid.NewGuid().ToString("N");
        var pk = Constants.Pk.Slots(league, "10U");
        var repository = new SlotRepository(service, NullLogger<SlotRepository>.Instance);
        try
        {
            await table.AddEntityAsync(new TableEntity(pk, "original") { ["Status"] = "Confirmed" });
            await table.AddEntityAsync(new TableEntity(pk, "replacement") { ["Status"] = "Open" });
            var original = await repository.GetSlotAsync(league, "10U", "original");
            var staleReplacement = await repository.GetSlotAsync(league, "10U", "replacement");
            var competing = await repository.GetSlotAsync(league, "10U", "replacement");
            competing!["Status"] = "Confirmed";
            await repository.UpdateSlotAsync(competing, competing.ETag);
            original!["Status"] = "Cancelled";
            staleReplacement!["Status"] = "Confirmed";

            var error = await Assert.ThrowsAsync<TableTransactionFailedException>(() =>
                repository.UpdateSlotsAtomicallyAsync(new[] { original, staleReplacement }));
            Assert.Equal(412, error.Status);
            Assert.Equal("Confirmed", (await repository.GetSlotAsync(league, "10U", "original"))!.GetString("Status"));
        }
        finally
        {
            // Delete only rows owned by this test, never the shared table or emulator directory.
            await table.DeleteEntityAsync(pk, "original", ETag.All);
            await table.DeleteEntityAsync(pk, "replacement", ETag.All);
        }
    }

    [StorageFact]
    public async Task SlotQueriesCannotReturnAnotherLeagueAndFollowContinuationPages()
    {
        var service = new TableServiceClient("UseDevelopmentStorage=true");
        var table = service.GetTableClient(Constants.Tables.Slots);
        await table.CreateIfNotExistsAsync();
        var league = "test-" + Guid.NewGuid().ToString("N");
        var otherLeague = league + "other";
        var pk = Constants.Pk.Slots(league, "10U");
        var otherPk = Constants.Pk.Slots(otherLeague, "10U");
        try
        {
            for (var i = 0; i < 3; i++) await table.AddEntityAsync(new TableEntity(pk, i.ToString()) { ["Status"] = "Open" });
            await table.AddEntityAsync(new TableEntity(otherPk, "foreign") { ["Status"] = "Open" });
            var repository = new SlotRepository(service, NullLogger<SlotRepository>.Instance);
            var rows = await repository.QueryAllSlotsAsync(new SlotQueryFilter { LeagueId = league, PageSize = 1 });
            Assert.Equal(3, rows.Count);
            Assert.All(rows, row => Assert.Equal(pk, row.PartitionKey));
        }
        finally
        {
            for (var i = 0; i < 3; i++) await table.DeleteEntityAsync(pk, i.ToString(), ETag.All);
            await table.DeleteEntityAsync(otherPk, "foreign", ETag.All);
        }
    }
}
