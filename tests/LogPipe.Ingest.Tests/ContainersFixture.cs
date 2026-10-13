using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;
using Testcontainers.Redis;

namespace LogPipe.Ingest.Tests;

public sealed class ContainersFixture : IAsyncLifetime
{
    public RedisContainer Redis { get; } = new RedisBuilder()
        .WithImage("redis:7-alpine")
        .Build();

    public async Task InitializeAsync() => await Redis.StartAsync();

    public async Task DisposeAsync() => await Redis.DisposeAsync();

    public WebApplicationFactory<Program> CreateFactory(int rateLimitPerMinute = 1200)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"logpipe-oidc-{Guid.NewGuid():N}.db");
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("environment", Environments.Development);
            builder.UseSetting("Redis:Configuration", Redis.GetConnectionString());
            builder.UseSetting("Search:GrpcUrl", "http://localhost:1");
            builder.UseSetting("Oidc:Issuer", "http://localhost:8090");
            builder.UseSetting("Oidc:ClientSecret", "test-secret");
            builder.UseSetting("Oidc:DbPath", dbPath);
            builder.UseSetting("RateLimit:EntriesPerMinute", rateLimitPerMinute.ToString());
            builder.UseSetting("Batch:MaxDelayMs", "10");
        });
    }
}

[CollectionDefinition("containers")]
public sealed class ContainersCollection : ICollectionFixture<ContainersFixture>;
