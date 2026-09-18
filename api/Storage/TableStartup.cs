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
        var tableNames = typeof(Constants.Tables).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!).Distinct(StringComparer.Ordinal);

        foreach (var tableName in tableNames)
        {
            var client = _serviceClient.GetTableClient(tableName);
            await client.CreateIfNotExistsAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
