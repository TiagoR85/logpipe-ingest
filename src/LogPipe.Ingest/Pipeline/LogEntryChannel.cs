using System.Threading.Channels;
using LogPipe.Contracts.V1;

namespace LogPipe.Ingest.Pipeline;

/// <summary>Bounded in-memory buffer between the REST ingress and the gRPC forwarder.</summary>
public sealed class LogEntryChannel
{
    private readonly Channel<LogEntry> _channel = Channel.CreateBounded<LogEntry>(
        new BoundedChannelOptions(10_000)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true
        });

    public ChannelReader<LogEntry> Reader => _channel.Reader;

    public bool TryWrite(LogEntry entry) => _channel.Writer.TryWrite(entry);
}
