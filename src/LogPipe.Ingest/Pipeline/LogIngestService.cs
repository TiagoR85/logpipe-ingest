using LogPipe.Contracts.Validation;
using LogPipe.Contracts.V1;
using LogPipe.Ingest.Api;

namespace LogPipe.Ingest.Pipeline;

public sealed record IngestOutcome(int Accepted, int Rejected, bool RateLimited, string? ValidationFailure);

public sealed class LogIngestService(
    LogEntryChannel channel,
    RateLimiter rateLimiter,
    Deduplicator deduplicator,
    ILogger<LogIngestService> logger)
{
    public async Task<IngestOutcome> IngestAsync(
        string clientId, IReadOnlyList<LogEntryDto> dtos, CancellationToken ct)
    {
        var proto = new List<(LogEntry Entry, bool IdFromClient)>(dtos.Count);
        foreach (var dto in dtos)
        {
            var idFromClient = !string.IsNullOrWhiteSpace(dto.Id);
            var entry = new LogEntry
            {
                Id = idFromClient ? dto.Id! : Guid.NewGuid().ToString(),
                Timestamp = string.IsNullOrWhiteSpace(dto.Timestamp)
                    ? DateTimeOffset.UtcNow.ToString("O")
                    : dto.Timestamp!,
                Level = dto.Level ?? "",
                Service = dto.Service ?? "",
                Message = dto.Message ?? "",
                TraceId = dto.TraceId ?? "",
                SpanId = dto.SpanId ?? ""
            };
            if (dto.Attributes is not null)
            {
                foreach (var (key, value) in dto.Attributes) entry.Attributes[key] = value;
            }

            if (!LogEntryRules.TryValidate(entry, out var reason))
            {
                logger.LogWarning("Rejected batch: {Reason}", reason);
                return new IngestOutcome(0, 0, false, reason);
            }

            proto.Add((entry, idFromClient));
        }

        if (!await rateLimiter.TryAcquireAsync(clientId, proto.Count, ct))
        {
            IngestMetrics.RateLimited.Add(proto.Count);
            return new IngestOutcome(0, 0, true, null);
        }

        var accepted = 0;
        var rejected = 0;
        foreach (var (entry, idFromClient) in proto)
        {
            if (await deduplicator.IsDuplicateAsync(entry, idFromClient, ct))
            {
                rejected++;
                IngestMetrics.Rejected.Add(1);
                continue;
            }

            if (!channel.TryWrite(entry))
            {
                rejected++;
                IngestMetrics.Dropped.Add(1);
                continue;
            }

            accepted++;
            IngestMetrics.Accepted.Add(1);
        }

        return new IngestOutcome(accepted, rejected, false, null);
    }
}
