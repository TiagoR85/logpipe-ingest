using System.Security.Cryptography;
using System.Text;
using LogPipe.Contracts.V1;
using StackExchange.Redis;

namespace LogPipe.Ingest.Pipeline;

/// <summary>1-hour duplicate window keyed by entry id, or by content hash when the id is client-omitted.</summary>
public sealed class Deduplicator(IDatabase redis)
{
    public async Task<bool> IsDuplicateAsync(LogEntry entry, bool idFromClient, CancellationToken ct)
    {
        var id = idFromClient ? entry.Id.ToLowerInvariant() : ContentHash(entry);
        var added = await redis.StringSetAsync($"dedup:{id}", "1", TimeSpan.FromHours(1), When.NotExists);
        return !added;
    }

    private static string ContentHash(LogEntry entry) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{entry.Timestamp}|{entry.Service}|{entry.Message}"))).ToLowerInvariant();
}
