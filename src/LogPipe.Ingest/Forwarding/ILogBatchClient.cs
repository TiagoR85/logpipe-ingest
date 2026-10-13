using LogPipe.Contracts.V1;

namespace LogPipe.Ingest.Forwarding;

public interface ILogBatchClient : IAsyncDisposable
{
    Task<StreamAck> SendAsync(IReadOnlyList<LogEntry> batch, CancellationToken ct);
}
