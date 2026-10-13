using System.Net;

namespace LogPipe.Ingest.Tests;

[Collection("containers")]
public sealed class SmokeTests
{
    private readonly ContainersFixture _fixture;
    public SmokeTests(ContainersFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Health_live_returns_200()
    {
        using var factory = _fixture.CreateFactory();
        var client = factory.CreateClient();
        var response = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Health_ready_returns_200_when_redis_connected()
    {
        using var factory = _fixture.CreateFactory();
        var client = factory.CreateClient();
        var response = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void Nlog_config_parses()
    {
        var cfg = new NLog.Config.XmlLoggingConfiguration(
            Path.Combine(AppContext.BaseDirectory, "nlog.config"));
        Assert.NotNull(cfg);
        Assert.NotEmpty(cfg.AllTargets);
    }

    [Fact]
    public void Ingest_metrics_expose_expected_counters()
    {
        Assert.Equal("LogPipe.Ingest", Pipeline.IngestMetrics.Meter.Name);
        Assert.NotNull(Pipeline.IngestMetrics.Accepted);
        Assert.NotNull(Pipeline.IngestMetrics.Rejected);
        Assert.NotNull(Pipeline.IngestMetrics.RateLimited);
        Assert.NotNull(Pipeline.IngestMetrics.Forwarded);
        Assert.NotNull(Pipeline.IngestMetrics.Dropped);
        Pipeline.IngestMetrics.Accepted.Add(1);
    }
}
