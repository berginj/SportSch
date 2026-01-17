using System.Security.Cryptography;
using System.Text;
using Azure;
using Azure.Data.Tables;
using GameSwap.Functions.Storage;
using Microsoft.Extensions.Logging;

namespace GameSwap.Functions.Services;

/// <summary>
/// Implementation of API key rotation service.
/// Uses Azure Table Storage to persist keys and rotation history.
/// </summary>
public class ApiKeyService : IApiKeyService
{
    private readonly TableServiceClient _tableService;
    private readonly ILogger<ApiKeyService> _logger;

    private const string API_KEYS_TABLE = "ApiKeys";
    private const string API_KEY_HISTORY_TABLE = "ApiKeyHistory";
    private const string PARTITION_KEY = "APIKEY";
    private const string ROW_KEY = "PRIMARY";

    public ApiKeyService(TableServiceClient tableService, ILogger<ApiKeyService> logger)
    {
        _tableService = tableService;
        _logger = logger;
    }

    public async Task<bool> ValidateKeyAsync(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return false;

        try
        {
            var keys = await GetActiveKeysAsync();

            // Hash the provided key and compare with stored hashes
            var providedKeyHash = HashApiKey(apiKey);

            var primaryHash = HashApiKey(keys.PrimaryKey);
            var secondaryHash = HashApiKey(keys.SecondaryKey);

            return providedKeyHash == primaryHash || providedKeyHash == secondaryHash;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate API key");
            return false;
        }
    }

    public async Task<ApiKeyInfo> GetActiveKeysAsync()
    {
        var table = await GetApiKeysTableAsync();

        try
        {
            var entity = (await table.GetEntityAsync<TableEntity>(PARTITION_KEY, ROW_KEY)).Value;

            return new ApiKeyInfo(
                PrimaryKey: entity.GetString("PrimaryKey") ?? "",
                SecondaryKey: entity.GetString("SecondaryKey") ?? "",
                PrimaryKeyCreatedUtc: entity.GetDateTimeOffset("PrimaryKeyCreatedUtc") ?? DateTimeOffset.UtcNow,
                SecondaryKeyCreatedUtc: entity.GetDateTimeOffset("SecondaryKeyCreatedUtc") ?? DateTimeOffset.UtcNow,
                LastRotatedBy: entity.GetString("LastRotatedBy") ?? "System",
                LastRotatedUtc: entity.GetDateTimeOffset("LastRotatedUtc") ?? DateTimeOffset.UtcNow
            );
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // Initialize keys if they don't exist
            _logger.LogWarning("API keys not found, initializing...");
            return await InitializeKeysAsync();
        }
    }

    public async Task<ApiKeyInfo> RotateKeysAsync(string performedBy)
    {
        var table = await GetApiKeysTableAsync();
        var now = DateTimeOffset.UtcNow;

        var currentKeys = await GetActiveKeysAsync();

        // Rotation: Secondary becomes Primary, generate new Secondary
        var newPrimaryKey = currentKeys.SecondaryKey;
        var newSecondaryKey = GenerateApiKey();

        var entity = new TableEntity(PARTITION_KEY, ROW_KEY)
        {
            ["PrimaryKey"] = newPrimaryKey,
            ["SecondaryKey"] = newSecondaryKey,
            ["PrimaryKeyCreatedUtc"] = currentKeys.SecondaryKeyCreatedUtc,
            ["SecondaryKeyCreatedUtc"] = now,
            ["LastRotatedBy"] = performedBy,
            ["LastRotatedUtc"] = now,
            ["UpdatedUtc"] = now
        };

        await table.UpsertEntityAsync(entity, TableUpdateMode.Replace);

        // Log rotation event
        await LogRotationEventAsync("ROTATE", performedBy, newPrimaryKey, newSecondaryKey);

        _logger.LogWarning("API keys rotated by {PerformedBy}", performedBy);

        return new ApiKeyInfo(
            PrimaryKey: newPrimaryKey,
            SecondaryKey: newSecondaryKey,
            PrimaryKeyCreatedUtc: currentKeys.SecondaryKeyCreatedUtc,
            SecondaryKeyCreatedUtc: now,
            LastRotatedBy: performedBy,
            LastRotatedUtc: now
        );
    }

    public async Task<ApiKeyInfo> RegenerateSecondaryKeyAsync(string performedBy)
    {
        var table = await GetApiKeysTableAsync();
        var now = DateTimeOffset.UtcNow;

        var currentKeys = await GetActiveKeysAsync();

        // Keep primary, regenerate secondary
        var newSecondaryKey = GenerateApiKey();

        var entity = new TableEntity(PARTITION_KEY, ROW_KEY)
        {
            ["PrimaryKey"] = currentKeys.PrimaryKey,
            ["SecondaryKey"] = newSecondaryKey,
            ["PrimaryKeyCreatedUtc"] = currentKeys.PrimaryKeyCreatedUtc,
            ["SecondaryKeyCreatedUtc"] = now,
            ["LastRotatedBy"] = performedBy,
            ["LastRotatedUtc"] = now,
            ["UpdatedUtc"] = now
        };

        await table.UpsertEntityAsync(entity, TableUpdateMode.Replace);

        // Log regeneration event
        await LogRotationEventAsync("REGENERATE_SECONDARY", performedBy, currentKeys.PrimaryKey, newSecondaryKey);

        _logger.LogWarning("Secondary API key regenerated by {PerformedBy}", performedBy);

        return new ApiKeyInfo(
            PrimaryKey: currentKeys.PrimaryKey,
            SecondaryKey: newSecondaryKey,
            PrimaryKeyCreatedUtc: currentKeys.PrimaryKeyCreatedUtc,
            SecondaryKeyCreatedUtc: now,
            LastRotatedBy: performedBy,
            LastRotatedUtc: now
        );
    }

    public async Task<List<ApiKeyRotationEvent>> GetRotationHistoryAsync(int limit = 10)
    {
        var table = await GetHistoryTableAsync();
        var events = new List<ApiKeyRotationEvent>();

        var query = table.QueryAsync<TableEntity>(
            filter: $"PartitionKey eq '{PARTITION_KEY}'",
            maxPerPage: limit
        );

        await foreach (var page in query.AsPages())
        {
            foreach (var entity in page.Values.OrderByDescending(e => e.Timestamp).Take(limit))
            {
                events.Add(new ApiKeyRotationEvent(
                    EventId: entity.RowKey,
                    EventType: entity.GetString("EventType") ?? "UNKNOWN",
                    PerformedBy: entity.GetString("PerformedBy") ?? "Unknown",
                    PerformedUtc: entity.Timestamp ?? DateTimeOffset.UtcNow,
                    PrimaryKeyHash: entity.GetString("PrimaryKeyHash") ?? "",
                    SecondaryKeyHash: entity.GetString("SecondaryKeyHash") ?? ""
                ));
            }
            break; // Only take first page
        }

        return events.OrderByDescending(e => e.PerformedUtc).ToList();
    }

    private async Task<ApiKeyInfo> InitializeKeysAsync()
    {
        var table = await GetApiKeysTableAsync();
        var now = DateTimeOffset.UtcNow;

        var primaryKey = GenerateApiKey();
        var secondaryKey = GenerateApiKey();

        var entity = new TableEntity(PARTITION_KEY, ROW_KEY)
        {
            ["PrimaryKey"] = primaryKey,
            ["SecondaryKey"] = secondaryKey,
            ["PrimaryKeyCreatedUtc"] = now,
            ["SecondaryKeyCreatedUtc"] = now,
            ["LastRotatedBy"] = "System",
            ["LastRotatedUtc"] = now,
            ["UpdatedUtc"] = now
        };

        await table.UpsertEntityAsync(entity, TableUpdateMode.Replace);

        await LogRotationEventAsync("INITIALIZE", "System", primaryKey, secondaryKey);

        _logger.LogWarning("API keys initialized");

        return new ApiKeyInfo(
            PrimaryKey: primaryKey,
            SecondaryKey: secondaryKey,
            PrimaryKeyCreatedUtc: now,
            SecondaryKeyCreatedUtc: now,
            LastRotatedBy: "System",
            LastRotatedUtc: now
        );
    }

    private async Task LogRotationEventAsync(string eventType, string performedBy, string primaryKey, string secondaryKey)
    {
        var table = await GetHistoryTableAsync();
        var eventId = $"{DateTimeOffset.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}";

        var entity = new TableEntity(PARTITION_KEY, eventId)
        {
            ["EventType"] = eventType,
            ["PerformedBy"] = performedBy,
            ["PerformedUtc"] = DateTimeOffset.UtcNow,
            ["PrimaryKeyHash"] = HashApiKey(primaryKey),
            ["SecondaryKeyHash"] = HashApiKey(secondaryKey)
        };

        await table.AddEntityAsync(entity);
    }

    private static string GenerateApiKey()
    {
        // Generate 32-byte (256-bit) random key
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes);
    }

    private static string HashApiKey(string apiKey)
    {
        var bytes = Encoding.UTF8.GetBytes(apiKey);
        var hash = SHA256.HashData(bytes);
        return Convert.ToBase64String(hash);
    }

    private async Task<TableClient> GetApiKeysTableAsync()
    {
        return await TableClients.GetTableAsync(_tableService, API_KEYS_TABLE);
    }

    private async Task<TableClient> GetHistoryTableAsync()
    {
        return await TableClients.GetTableAsync(_tableService, API_KEY_HISTORY_TABLE);
    }
}
