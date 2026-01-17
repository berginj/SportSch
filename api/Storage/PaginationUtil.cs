using Azure.Data.Tables;

namespace GameSwap.Functions.Storage;

/// <summary>
/// Result of a paginated query.
/// </summary>
public class PaginationResult<T>
{
    public List<T> Items { get; set; } = new();
    public string? ContinuationToken { get; set; }
    public int PageSize { get; set; }
    public bool HasMore => !string.IsNullOrEmpty(ContinuationToken);
}

/// <summary>
/// Utilities for paginating Azure Table Storage queries.
/// </summary>
public static class PaginationUtil
{
    /// <summary>
    /// Queries table storage with pagination support.
    /// </summary>
    /// <param name="table">Table client</param>
    /// <param name="filter">OData filter string</param>
    /// <param name="continuationToken">Optional continuation token from previous page</param>
    /// <param name="pageSize">Maximum items per page (default 50)</param>
    /// <returns>Paginated result with items and continuation token</returns>
    public static async Task<PaginationResult<TableEntity>> QueryWithPaginationAsync(
        TableClient table,
        string? filter,
        string? continuationToken = null,
        int pageSize = 50)
    {
        var result = new PaginationResult<TableEntity>
        {
            PageSize = pageSize
        };

        await foreach (var page in table.QueryAsync<TableEntity>(filter: filter).AsPages(continuationToken, pageSize))
        {
            result.Items.AddRange(page.Values);
            result.ContinuationToken = page.ContinuationToken;

            // Only return the first page
            break;
        }

        return result;
    }

    /// <summary>
    /// Queries table storage with pagination and maps results.
    /// </summary>
    public static async Task<PaginationResult<TResult>> QueryWithPaginationAsync<TResult>(
        TableClient table,
        string? filter,
        Func<TableEntity, TResult> mapper,
        string? continuationToken = null,
        int pageSize = 50)
    {
        var entityResult = await QueryWithPaginationAsync(table, filter, continuationToken, pageSize);

        return new PaginationResult<TResult>
        {
            Items = entityResult.Items.Select(mapper).ToList(),
            ContinuationToken = entityResult.ContinuationToken,
            PageSize = entityResult.PageSize
        };
    }
}
