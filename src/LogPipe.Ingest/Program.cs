using LogPipe.Ingest.Pipeline;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NLog;
using NLog.Web;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// Fail-fast configuration: the host never starts half-configured (same rule as einvoice-worker).
string[] required = ["Redis:Configuration", "Search:GrpcUrl", "Oidc:Issuer"];
var missing = required
    .Where(key => string.IsNullOrWhiteSpace(builder.Configuration[key]))
    .ToArray();
if (missing.Length > 0)
{
    throw new InvalidOperationException(
        $"Missing required configuration: {string.Join(", ", missing)}. See .env.example.");
}

builder.Logging.ClearProviders();
builder.Host.UseNLog();

builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(builder.Configuration["Redis:Configuration"]!));

builder.Services.AddSingleton<RedisHealthCheck>();
builder.Services.AddHealthChecks()
    .AddCheck("self", static () => HealthCheckResult.Healthy(), tags: ["live"])
    .AddCheck<RedisHealthCheck>("redis", tags: ["ready"]);

builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddRuntimeInstrumentation()
        .AddMeter("LogPipe.Ingest")
        .AddPrometheusExporter())
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddGrpcClientInstrumentation()
        .AddSource("LogPipe.Ingest"));

var app = builder.Build();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = static r => r.Tags.Contains("live") });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = static r => r.Tags.Contains("ready") });
app.UseOpenTelemetryPrometheusScrapingEndpoint("/metrics");

app.Run();

public sealed class RedisHealthCheck(IConnectionMultiplexer redis) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(redis.IsConnected
            ? HealthCheckResult.Healthy("redis connected")
            : HealthCheckResult.Unhealthy("redis disconnected"));
}

public partial class Program;
