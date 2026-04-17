using Azure.Data.Tables;

namespace GameSwap.Functions.Services;

/// <summary>
/// Service for managing API key rotation and validation.
/// Implements a dual-key system for zero-downtime rotation.
/// </summary>
public interface IApiKeyService
{
    /// <summary>
    /// Validates an API key.
    /// </summary>
    /// <param name="apiKey">The API key to validate</param>
    /// <returns>True if the key is valid and not expired</returns>
    Task<bool> ValidateKeyAsync(string apiKey);

    /// <summary>
    /// Gets the current active API keys (primary and secondary).
    /// Note: Keys are stored as hashes. This returns the hashes for internal validation.
    /// Plaintext keys are only returned during rotation/regeneration operations.
    /// </summary>
    /// <returns>API key information with hashed keys</returns>
    Task<ApiKeyInfo> GetActiveKeysAsync();

    /// <summary>
    /// Rotates the API keys. Moves secondary to primary, generates new secondary.
    /// WARNING: The new secondary key is returned in plaintext ONLY ONCE. Save it immediately.
    /// </summary>
    /// <param name="performedBy">User ID performing the rotation</param>
    /// <returns>API key information with new secondary key in plaintext (shown only once)</returns>
    Task<ApiKeyInfo> RotateKeysAsync(string performedBy);

    /// <summary>
    /// Regenerates the secondary key without rotating.
    /// Useful for emergency revocation.
    /// WARNING: The new secondary key is returned in plaintext ONLY ONCE. Save it immediately.
    /// </summary>
    /// <param name="performedBy">User ID performing the regeneration</param>
    /// <returns>API key information with new secondary key in plaintext (shown only once)</returns>
    Task<ApiKeyInfo> RegenerateSecondaryKeyAsync(string performedBy);

    /// <summary>
    /// Gets the rotation history for audit purposes.
    /// </summary>
    /// <param name="limit">Maximum number of history entries to return</param>
    /// <returns>List of rotation events</returns>
    Task<List<ApiKeyRotationEvent>> GetRotationHistoryAsync(int limit = 10);
}

public record ApiKeyInfo(
    string PrimaryKey,
    string SecondaryKey,
    DateTimeOffset PrimaryKeyCreatedUtc,
    DateTimeOffset SecondaryKeyCreatedUtc,
    string LastRotatedBy,
    DateTimeOffset LastRotatedUtc
);

public record ApiKeyRotationEvent(
    string EventId,
    string EventType,
    string PerformedBy,
    DateTimeOffset PerformedUtc,
    string PrimaryKeyHash,
    string SecondaryKeyHash
);
