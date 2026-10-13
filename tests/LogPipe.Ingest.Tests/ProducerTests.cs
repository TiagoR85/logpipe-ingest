using System.Net;
using System.Net.Http.Json;
using LogPipe.SampleProducer;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace LogPipe.Ingest.Tests;

public sealed class ProducerTests
{
    [Fact]
    public async Task TokenProvider_caches_token_until_expiry()
    {
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/connect/token").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBodyAsJson(new { access_token = "tok-123", token_type = "Bearer", expires_in = 300 }));

        var options = new ProducerOptions
        {
            OidcBaseUrl = server.Url!, IngestBaseUrl = server.Url!,
            ClientId = "logpipe-writer", ClientSecret = "s",
            IntervalSeconds = 1, BatchSize = 1
        };
        using var http = new HttpClient();
        var provider = new TokenProvider(http, options,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<TokenProvider>.Instance);

        Assert.Equal("tok-123", await provider.GetTokenAsync(CancellationToken.None));
        Assert.Equal("tok-123", await provider.GetTokenAsync(CancellationToken.None));

        Assert.Equal(1, server.LogEntries.Count(e => e.RequestMessage.Path == "/connect/token"));
    }

    [Fact]
    public async Task Sender_posts_batch_with_bearer_token_and_returns_accepted()
    {
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/connect/token").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBodyAsJson(new { access_token = "tok-abc", token_type = "Bearer", expires_in = 300 }));
        server
            .Given(Request.Create().WithPath("/v1/logs").UsingPost()
                .WithHeader("Authorization", "Bearer tok-abc"))
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBodyAsJson(new { accepted = 2, rejected = 0 }));

        var options = new ProducerOptions
        {
            OidcBaseUrl = server.Url!, IngestBaseUrl = server.Url!,
            ClientId = "logpipe-writer", ClientSecret = "s",
            IntervalSeconds = 1, BatchSize = 2
        };
        using var http = new HttpClient();
        var tokens = new TokenProvider(http, options,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<TokenProvider>.Instance);
        var sender = new BatchLogSender(http, tokens, options,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<BatchLogSender>.Instance);

        var batch = new LogGenerator(new Random(42)).NextBatch(2);
        var accepted = await sender.SendAsync(batch, CancellationToken.None);

        Assert.Equal(2, accepted);
        var logCall = server.LogEntries.Single(e => e.RequestMessage.Path == "/v1/logs");
        var body = logCall.RequestMessage.BodyAsJson?.ToString() ?? "";
        Assert.Contains("entries", body);
    }

    [Fact]
    public void Generator_produces_valid_batches_with_unique_ids_and_known_levels()
    {
        var generator = new LogGenerator(new Random(7));
        var batch = generator.NextBatch(20);

        Assert.Equal(20, batch.Count);
        Assert.Equal(20, batch.Select(e => e.Id).Distinct().Count());
        var allowed = new HashSet<string> { "Trace", "Debug", "Information", "Warning", "Error", "Critical" };
        Assert.All(batch, e => Assert.Contains(e.Level, allowed));
        Assert.Contains(batch, e => !string.IsNullOrEmpty(e.TraceId)); // bursts exist for the trace demo
    }

    [Fact]
    public void Generator_respects_batch_size_even_when_burst_larger()
    {
        var generator = new LogGenerator(new Random(1));
        Assert.Equal(3, generator.NextBatch(3).Count);
    }
}
