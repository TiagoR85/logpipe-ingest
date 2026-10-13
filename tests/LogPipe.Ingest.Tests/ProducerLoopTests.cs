using LogPipe.SampleProducer;
using Microsoft.Extensions.Logging.Abstractions;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace LogPipe.Ingest.Tests;

public sealed class ProducerLoopTests
{
    private static void StubToken(WireMockServer server) =>
        server.Given(Request.Create().WithPath("/connect/token").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBodyAsJson(new { access_token = "tok", token_type = "Bearer", expires_in = 300 }));

    private static ProducerOptions OptionsFor(WireMockServer server) => new()
    {
        OidcBaseUrl = server.Url!, IngestBaseUrl = server.Url!,
        ClientId = "c", ClientSecret = "s",
        IntervalSeconds = 0, BatchSize = 1
    };

    [Fact]
    public async Task Loop_sends_batches_until_cancelled()
    {
        using var server = WireMockServer.Start();
        StubToken(server);
        server.Given(Request.Create().WithPath("/v1/logs").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBodyAsJson(new { accepted = 1, rejected = 0 }));

        var loop = new ProducerLoop(
            new BatchLogSender(new HttpClient(),
                new TokenProvider(new HttpClient(), OptionsFor(server), NullLogger<TokenProvider>.Instance),
                OptionsFor(server), NullLogger<BatchLogSender>.Instance),
            new LogGenerator(new Random(0)), OptionsFor(server), NullLogger<ProducerLoop>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await loop.StartAsync(cts.Token);
        await WaitUntilAsync(() => server.LogEntries.Count(e => e.RequestMessage.Path == "/v1/logs") >= 2,
            TimeSpan.FromSeconds(5));
        await cts.CancelAsync();
        await loop.StopAsync(CancellationToken.None);

        Assert.True(server.LogEntries.Count(e => e.RequestMessage.Path == "/v1/logs") >= 2);
    }

    [Fact]
    public async Task Loop_survives_ingest_failure_and_keeps_sending()
    {
        using var server = WireMockServer.Start();
        StubToken(server);
        // /v1/logs always 500 -> every send takes the failure path; the loop must keep running.
        server.Given(Request.Create().WithPath("/v1/logs").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(500));

        var loop = new ProducerLoop(
            new BatchLogSender(new HttpClient(),
                new TokenProvider(new HttpClient(), OptionsFor(server), NullLogger<TokenProvider>.Instance),
                OptionsFor(server), NullLogger<BatchLogSender>.Instance),
            new LogGenerator(new Random(0)), OptionsFor(server), NullLogger<ProducerLoop>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await loop.StartAsync(cts.Token);
        // Every cycle re-posts despite the 500, so >= 3 posts proves the loop survived the failures.
        await WaitUntilAsync(
            () => server.LogEntries.Count(e => e.RequestMessage.Path == "/v1/logs") >= 3,
            TimeSpan.FromSeconds(5));
        await cts.CancelAsync();
        await loop.StopAsync(CancellationToken.None);

        Assert.True(server.LogEntries.Count(e => e.RequestMessage.Path == "/v1/logs") >= 3);
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
