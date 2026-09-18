using System.Collections.Concurrent;

namespace GameSwap.Functions.Services;

/// <summary>
/// Local sliding-window rate limiter for the low-cost MVP stack.
/// </summary>
public class InMemoryRateLimitService : IRateLimitService
{
    private const int MaxRequestsPerMinute = 100;
    private const int WindowSizeSeconds = 60;

    private readonly ConcurrentDictionary<string, Queue<long>> _requests = new();
    private readonly ConcurrentDictionary<string, object> _locks = new();

    public Task<bool> IsAllowedAsync(string identifier)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var gate = _locks.GetOrAdd(identifier, _ => new object());

        lock (gate)
        {
            var queue = _requests.GetOrAdd(identifier, _ => new Queue<long>());
            Prune(queue, now);

            if (queue.Count >= MaxRequestsPerMinute)
            {
                return Task.FromResult(false);
            }

            queue.Enqueue(now);
            return Task.FromResult(true);
        }
    }

    public Task<RateLimitInfo> GetRateLimitInfoAsync(string identifier)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var gate = _locks.GetOrAdd(identifier, _ => new object());

        lock (gate)
        {
            var queue = _requests.GetOrAdd(identifier, _ => new Queue<long>());
            Prune(queue, now);

            var remaining = Math.Max(0, MaxRequestsPerMinute - queue.Count);
            var reset = queue.Count > 0
                ? queue.Peek() + WindowSizeSeconds
                : now + WindowSizeSeconds;

            return Task.FromResult(new RateLimitInfo(MaxRequestsPerMinute, remaining, reset));
        }
    }

    private static void Prune(Queue<long> queue, long now)
    {
        var windowStart = now - WindowSizeSeconds;
        while (queue.Count > 0 && queue.Peek() <= windowStart)
        {
            queue.Dequeue();
        }
    }
}
