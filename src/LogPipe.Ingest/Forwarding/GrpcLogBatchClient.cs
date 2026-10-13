using Grpc.Net.Client;
using LogPipe.Contracts.V1;

namespace LogPipe.Ingest.Forwarding;

public sealed class GrpcLogBatchClient : ILogBatchClient
{
    private readonly GrpcChannel _channel;
    private readonly LogIngest.LogIngestClient _client;

    public GrpcLogBatchClient(IConfiguration config)
    {
        // BaseAddress-style footgun from einvoice-worker f3ee1f2: read the URL here, once.
        var url = config["Search:GrpcUrl"]
            ?? throw new InvalidOperationException("Search:GrpcUrl is required.");
        _channel = GrpcChannel.ForAddress(url);
        _client = new LogIngest.LogIngestClient(_channel);
    }

    public async Task<StreamAck> SendAsync(IReadOnlyList<LogEntry> batch, CancellationToken ct)
    {
        using var call = _client.StreamLogs(cancellationToken: ct);
        foreach (var entry in batch)
        {
            await call.RequestStream.WriteAsync(entry, ct);
        }
        await call.RequestStream.CompleteAsync();
        return await call.ResponseAsync;
    }

    public async ValueTask DisposeAsync() => await _channel.ShutdownAsync();
}
