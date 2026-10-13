using Microsoft.Extensions.Configuration;

namespace LogPipe.SampleProducer;

public sealed class ProducerOptions
{
    public required string OidcBaseUrl { get; init; }
    public required string IngestBaseUrl { get; init; }
    public required string ClientId { get; init; }
    public required string ClientSecret { get; init; }
    public int IntervalSeconds { get; init; } = 2;
    public int BatchSize { get; init; } = 15;

    public static ProducerOptions FromConfiguration(IConfiguration config)
    {
        var secret = config["Oidc:ClientSecret"];
        if (string.IsNullOrWhiteSpace(secret))
            throw new InvalidOperationException("Oidc:ClientSecret is required (env Oidc__ClientSecret).");
        return new ProducerOptions
        {
            OidcBaseUrl = config["Oidc:BaseUrl"] ?? "http://localhost:8090",
            IngestBaseUrl = config["Ingest:BaseUrl"] ?? "http://localhost:8090",
            ClientId = config["Oidc:ClientId"] ?? "logpipe-writer",
            ClientSecret = secret,
            IntervalSeconds = config.GetValue("Producer:IntervalSeconds", 2),
            BatchSize = config.GetValue("Producer:BatchSize", 15)
        };
    }
}
