using System.Security.Claims;
using LogPipe.Ingest.Api;
using LogPipe.Ingest.Auth;
using LogPipe.Ingest.Pipeline;
using LogPipe.Ingest.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NLog;
using NLog.Web;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using OpenIddict.Server.AspNetCore;
using OpenIddict.Validation.AspNetCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using StackExchange.Redis;
using static OpenIddict.Abstractions.OpenIddictConstants;

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

// OIDC issuer (client_credentials). OpenIddict 7 has no in-memory stores, so the
// client store is a SQLite file — self-contained, no Postgres in F2 (ADR-0001).
builder.Services.AddDbContext<OidcDbContext>(options =>
{
    var dbPath = builder.Configuration["Oidc:DbPath"] ?? "openiddict.db";
    options.UseSqlite($"Data Source={dbPath}");
    options.UseOpenIddict();
});

builder.Services.AddOpenIddict()
    .AddCore(options => options
        .UseEntityFrameworkCore()
        .UseDbContext<OidcDbContext>())
    .AddServer(options =>
    {
        options.SetTokenEndpointUris("/connect/token");
        options.AllowClientCredentialsFlow();
        options.RegisterScopes("logs:read", "logs:write");
        options.AddDevelopmentEncryptionCertificate();
        options.AddDevelopmentSigningCertificate();
        options.DisableAccessTokenEncryption();
        options.SetIssuer(builder.Configuration["Oidc:Issuer"]!);
        options.AddEventHandler<OpenIddictServerEvents.HandleTokenRequestContext>(descriptor =>
            descriptor.UseInlineHandler(context =>
            {
                if (context.Request.IsClientCredentialsGrantType())
                {
                    var identity = new ClaimsIdentity(
                        OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                        Claims.Name,
                        Claims.Role);
                    identity.SetClaim(Claims.Subject, context.Request.ClientId!);
                    var principal = new ClaimsPrincipal(identity);
                    principal.SetScopes(context.Request.GetScopes());
                    context.Principal = principal;
                }
                return ValueTask.CompletedTask;
            }));
        options.UseAspNetCore().DisableTransportSecurityRequirement();
    })
    .AddValidation(options =>
    {
        options.UseLocalServer();
        options.UseAspNetCore();
    });

builder.Services.AddAuthentication(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
builder.Services.AddAuthorization(options => options.AddLogPipePolicies());

builder.Services.AddSingleton<LogEntryChannel>();
builder.Services.AddSingleton<LogIngestService>();

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

app.MapLogEndpoints();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = static r => r.Tags.Contains("live") });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = static r => r.Tags.Contains("ready") });
app.UseOpenTelemetryPrometheusScrapingEndpoint("/metrics");

await OpenIddictSeed.EnsureSeededAsync(app.Services);

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
