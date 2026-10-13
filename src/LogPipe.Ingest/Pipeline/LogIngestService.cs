using LogPipe.Contracts.Validation;
using LogPipe.Contracts.V1;
using LogPipe.Ingest.Api;

namespace LogPipe.Ingest.Pipeline;

public sealed record IngestOutcome(int Accepted, int Rejected, bool RateLimited, string? ValidationFailure);

public sealed class LogIngestService(LogEntryChannel channel)
{
    public Task<IngestOutcome> IngestAsync(string clientId, IReadOnlyList<LogEntryDto> dtos, CancellationToken ct)
    {
        var proto = new List<LogEntry>(dtos.Count);
        foreach (var dto in dtos)
        {
            var entry = new LogEntry
            {
                Id = string.IsNullOrWhiteSpace(dto.Id) ? Guid.NewGuid().ToString() : dto.Id!,
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
                return Task.FromResult(new IngestOutcome(0, 0, false, reason));

            proto.Add(entry);
        }

        var accepted = 0;
        foreach (var entry in proto)
        {
            if (channel.TryWrite(entry)) accepted++;
        }

        return Task.FromResult(new IngestOutcome(accepted, proto.Count - accepted, false, null));
    }
}
