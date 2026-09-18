using Azure.Data.Tables;
using Microsoft.Extensions.Configuration;

namespace GameSwap.Functions.Storage;

public static class TableClients
{
    public static TableServiceClient CreateServiceClient(IConfiguration config)
    {
        var conn = config["GameSwapStorage"];
        if (string.IsNullOrWhiteSpace(conn))
            conn = config["AzureWebJobsStorage"];
        if (string.IsNullOrWhiteSpace(conn))
            throw new InvalidOperationException("Missing GameSwapStorage/AzureWebJobsStorage setting.");
        return new TableServiceClient(conn);
    }

    /// <summary>
    /// Returns a client without a network call. TableStartup/provisioning owns table creation.
    /// </summary>
    public static Task<TableClient> GetTableAsync(TableServiceClient svc, string tableName)
    {
        var client = svc.GetTableClient(tableName);
        return Task.FromResult(client);
    }
}
