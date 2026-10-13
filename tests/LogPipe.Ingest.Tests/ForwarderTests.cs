using Grpc.Core;
using LogPipe.Contracts.V1;
using LogPipe.Ingest.Forwarding;
using LogPipe.Ingest.Pipeline;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace LogPipe.Ingest.Tests;

public sealed class ForwarderTests
{
    private sealed class RecordingClient : ILogBatchClient
    {
        public List<IReadOnlyList<LogEntry>> Batches { get; } = [];
        public int Attempts { get; private set; }
        public int FailuresToThrow { get; set; }

        public Task<StreamAck> SendAsync(IReadOnlyList<LogEntry> batch, CancellationToken ct)
        {
            Attempts++;
            if (FailuresToThrow > 0)
            {
                FailuresToThrow--;
                throw new RpcException(new Status(StatusCode.Unavailable, "search is down"));
            }
            Batches.Add([.. batch]);
            return Task.FromResult(new StreamAck { Accepted = batch.Count });
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static LogEntry NewEntry() => new()
    {
        Id = Guid.NewGuid().ToString(),
        Timestamp = DateTimeOffset.UtcNow.ToString("O"),
        Level = "Information",
        Service = "forwarder-tests",
        Message = "ping"
    };

    private static IConfiguration Config(int maxSize = 5, int maxDelayMs = 10, int attempts = 3) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Batch:MaxSize"] = maxSize.ToString(),
            ["Batch:MaxDelayMs"] = maxDelayMs.ToString(),
            ["Batch:MaxAttempts"] = attempts.ToString()
        }).Build();

    [Fact]
    public async Task Drains_channel_in_batches_of_max_size()
    {
        var channel = new LogEntryChannel();
        var client = new RecordingClient();
        var forwarder = new BatchForwarder(channel, client, Config(), NullLogger<BatchForwarder>.Instance);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await forwarder.StartAsync(cts.Token);

        for (var i = 0; i < 7; i++) Assert.True(channel.TryWrite(NewEntry()));

        await WaitUntilAsync(() => client.Batches.Sum(b => b.Count) == 7, TimeSpan.FromSeconds(15));
        Assert.Contains(client.Batches, b => b.Count == 5);
        await cts.CancelAsync();
        await forwarder.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Retries_failed_batches_and_gives_up_after_max_attempts()
    {
        var channel = new LogEntryChannel();
        var client = new RecordingClient { FailuresToThrow = int.MaxValue };
        var forwarder = new BatchForwarder(channel, client, Config(attempts: 3), NullLogger<BatchForwarder>.Instance);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await forwarder.StartAsync(cts.Token);

        Assert.True(channel.TryWrite(NewEntry()));
        await WaitUntilAsync(() => client.Attempts >= 3, TimeSpan.FromSeconds(5));
        await Task.Delay(300);
        Assert.Equal(3, client.Attempts);
        await cts.CancelAsync();
        await forwarder.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Recovers_after_transient_failures()
    {
        var channel = new LogEntryChannel();
        var client = new RecordingClient { FailuresToThrow = 2 };
        var forwarder = new BatchForwarder(channel, client, Config(), NullLogger<BatchForwarder>.Instance);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await forwarder.StartAsync(cts.Token);

        Assert.True(channel.TryWrite(NewEntry()));
        await WaitUntilAsync(() => client.Batches.Count == 1, TimeSpan.FromSeconds(5));
        Assert.Single(client.Batches[0]);
        await cts.CancelAsync();
        await forwarder.StopAsync(CancellationToken.None);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("condition not met in time");
            await Task.Delay(25);
        }
    }
}
