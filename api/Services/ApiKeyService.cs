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

            // Hash the provided key and compare with stored hashes (keys are already hashed in DB)
            var providedKeyHash = HashApiKey(apiKey);

            return providedKeyHash == keys.PrimaryKey || providedKeyHash == keys.SecondaryKey;
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

        // Rotation: Secondary becomes Primary, generate new Secondary (plaintext)
        var newPrimaryKeyHash = currentKeys.SecondaryKey; // Already hashed
        var newSecondaryKeyPlaintext = GenerateApiKey();
        var newSecondaryKeyHash = HashApiKey(newSecondaryKeyPlaintext);

        var entity = new TableEntity(PARTITION_KEY, ROW_KEY)
        {
            ["PrimaryKey"] = newPrimaryKeyHash,
            ["SecondaryKey"] = newSecondaryKeyHash,
            ["PrimaryKeyCreatedUtc"] = currentKeys.SecondaryKeyCreatedUtc,
            ["SecondaryKeyCreatedUtc"] = now,
            ["LastRotatedBy"] = performedBy,
            ["LastRotatedUtc"] = now,
            ["UpdatedUtc"] = now
        };

        await table.UpsertEntityAsync(entity, TableUpdateMode.Replace);

        // Log rotation event (already hashed)
        await LogRotationEventAsync("ROTATE", performedBy, newPrimaryKeyHash, newSecondaryKeyHash);

        _logger.LogWarning("API keys rotated by {PerformedBy}", performedBy);

        // Return metadata with the NEW secondary key in plaintext (shown only once!)
        // Primary is not returned in plaintext as it was the old secondary
        return new ApiKeyInfo(
            PrimaryKey: $"[Hash: {newPrimaryKeyHash.Substring(0, Math.Min(12, newPrimaryKeyHash.Length))}...]",
            SecondaryKey: newSecondaryKeyPlaintext, // ONLY TIME THIS IS SHOWN
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

        // Keep primary, regenerate secondary (plaintext)
        var newSecondaryKeyPlaintext = GenerateApiKey();
        var newSecondaryKeyHash = HashApiKey(newSecondaryKeyPlaintext);

        var entity = new TableEntity(PARTITION_KEY, ROW_KEY)
        {
            ["PrimaryKey"] = currentKeys.PrimaryKey, // Already hashed
            ["SecondaryKey"] = newSecondaryKeyHash,
            ["PrimaryKeyCreatedUtc"] = currentKeys.PrimaryKeyCreatedUtc,
            ["SecondaryKeyCreatedUtc"] = now,
            ["LastRotatedBy"] = performedBy,
            ["LastRotatedUtc"] = now,
            ["UpdatedUtc"] = now
        };

        await table.UpsertEntityAsync(entity, TableUpdateMode.Replace);

        // Log regeneration event (both hashed)
        await LogRotationEventAsync("REGENERATE_SECONDARY", performedBy, currentKeys.PrimaryKey, newSecondaryKeyHash);

        _logger.LogWarning("Secondary API key regenerated by {PerformedBy}", performedBy);

        // Return metadata with partial primary and full secondary (shown only once!)
        return new ApiKeyInfo(
            PrimaryKey: $"[Hash: {currentKeys.PrimaryKey.Substring(0, Math.Min(12, currentKeys.PrimaryKey.Length))}...]",
            SecondaryKey: newSecondaryKeyPlaintext, // ONLY TIME THIS IS SHOWN
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

        var primaryKeyPlaintext = GenerateApiKey();
        var secondaryKeyPlaintext = GenerateApiKey();
        var primaryKeyHash = HashApiKey(primaryKeyPlaintext);
        var secondaryKeyHash = HashApiKey(secondaryKeyPlaintext);

        var entity = new TableEntity(PARTITION_KEY, ROW_KEY)
        {
            ["PrimaryKey"] = primaryKeyHash,
            ["SecondaryKey"] = secondaryKeyHash,
            ["PrimaryKeyCreatedUtc"] = now,
            ["SecondaryKeyCreatedUtc"] = now,
            ["LastRotatedBy"] = "System",
            ["LastRotatedUtc"] = now,
            ["UpdatedUtc"] = now
        };

        await table.UpsertEntityAsync(entity, TableUpdateMode.Replace);

        await LogRotationEventAsync("INITIALIZE", "System", primaryKeyHash, secondaryKeyHash);

        _logger.LogWarning("API keys initialized - keys shown only once!");

        // Return plaintext keys ONLY during initialization
        return new ApiKeyInfo(
            PrimaryKey: primaryKeyPlaintext,
            SecondaryKey: secondaryKeyPlaintext,
            PrimaryKeyCreatedUtc: now,
            SecondaryKeyCreatedUtc: now,
            LastRotatedBy: "System",
            LastRotatedUtc: now
        );
    }

    private async Task LogRotationEventAsync(string eventType, string performedBy, string primaryKeyHash, string secondaryKeyHash)
    {
        var table = await GetHistoryTableAsync();
        var eventId = $"{DateTimeOffset.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}";

        var entity = new TableEntity(PARTITION_KEY, eventId)
        {
            ["EventType"] = eventType,
            ["PerformedBy"] = performedBy,
            ["PerformedUtc"] = DateTimeOffset.UtcNow,
            ["PrimaryKeyHash"] = primaryKeyHash, // Already hashed
            ["SecondaryKeyHash"] = secondaryKeyHash // Already hashed
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
