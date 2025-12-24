using Azure.Data.Tables;
using Microsoft.Extensions.Configuration;

namespace GameSwap.Functions.Storage;

public static class TableClients
{
    public static TableServiceClient CreateServiceClient(IConfiguration config)
    {
        var conn = config["AzureWebJobsStorage"];
        if (string.IsNullOrWhiteSpace(conn))
            throw new InvalidOperationException("Missing AzureWebJobsStorage setting.");
        return new TableServiceClient(conn);
    }

    /// <summary>
    /// Always ensures the target table exists before returning a client.
    /// Safe to call on every request.
    /// </summary>
    public static async Task<TableClient> GetTableAsync(TableServiceClient svc, string tableName)
    {
        var client = svc.GetTableClient(tableName);
        await client.CreateIfNotExistsAsync();
        return client;
    }
}
