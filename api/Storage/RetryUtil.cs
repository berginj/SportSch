using Azure;

namespace GameSwap.Functions.Storage;

/// <summary>
/// Utilities for retrying operations with exponential backoff.
/// Primarily used for handling ETag concurrency conflicts in Table Storage.
/// </summary>
public static class RetryUtil
{
    /// <summary>
    /// Retries an operation if it fails due to ETag mismatch (HTTP 412).
    /// Uses exponential backoff between retries.
    /// </summary>
    /// <param name="operation">The operation to retry</param>
    /// <param name="maxRetries">Maximum number of retry attempts (default 3)</param>
    /// <param name="delayMs">Initial delay in milliseconds (default 100)</param>
    /// <returns>Result from the operation</returns>
    public static async Task<T> WithEtagRetryAsync<T>(
        Func<Task<T>> operation,
        int maxRetries = 3,
        int delayMs = 100)
    {
        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                return await operation();
            }
            catch (RequestFailedException ex) when (ex.Status == 412 && i < maxRetries - 1) // ETag mismatch
            {
                // Exponential backoff: 100ms, 200ms, 400ms, etc.
                await Task.Delay(delayMs * (int)Math.Pow(2, i));
            }
        }

        // If we get here, all retries failed
        throw new InvalidOperationException($"Operation failed after {maxRetries} retries");
    }

    /// <summary>
    /// Retries an operation without a return value if it fails due to ETag mismatch.
    /// </summary>
    public static async Task WithEtagRetryAsync(
        Func<Task> operation,
        int maxRetries = 3,
        int delayMs = 100)
    {
        await WithEtagRetryAsync(async () =>
        {
            await operation();
            return true; // Dummy return value
        }, maxRetries, delayMs);
    }
}
