using Azure.Data.Tables;
using GameSwap.Functions.Storage;

namespace GameSwap.Functions.Repositories;

public static class SlotRepositoryQueries
{
    // Correctness queries must not silently inspect only the first storage page.
    public static async Task<List<TableEntity>> QueryAllSlotsAsync(this ISlotRepository repository, SlotQueryFilter filter)
    {
        var items = new List<TableEntity>();
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        string? continuation = null;
        do
        {
            var page = await repository.QuerySlotsAsync(filter, continuation);
            items.AddRange(page.Items);
            continuation = page.ContinuationToken;
            if (!string.IsNullOrEmpty(continuation) && !tokens.Add(continuation))
                throw new InvalidOperationException("Storage returned a repeated continuation token.");
        } while (!string.IsNullOrEmpty(continuation));
        return items;
    }
}
