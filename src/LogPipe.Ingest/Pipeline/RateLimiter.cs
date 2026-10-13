using StackExchange.Redis;

namespace LogPipe.Ingest.Pipeline;

/// <summary>Fixed per-minute window per client, implemented with one Redis INCR key per window.</summary>
public sealed class RateLimiter(IDatabase redis, IConfiguration config, ILogger<RateLimiter> logger)
{
    public async Task<bool> TryAcquireAsync(string clientId, int entries, CancellationToken ct)
    {
        var limit = config.GetValue("RateLimit:EntriesPerMinute", 1200);
        var window = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmm");
        var key = $"rl:entries:{clientId}:{window}";

        var count = await redis.StringIncrementAsync(key, entries);
        if (count == entries)
        {
            // First hit in this window: arm a 2-minute expiry (covers clock skew between minutes).
            await redis.KeyExpireAsync(key, TimeSpan.FromMinutes(2));
        }

        if (count > limit)
        {
            logger.LogWarning("Rate limit exceeded for {ClientId}: {Count} > {Limit}", clientId, count, limit);
            return false;
        }

        return true;
    }
}
