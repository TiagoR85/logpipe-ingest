using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace LogPipe.SampleProducer;

public sealed class TokenProvider(HttpClient http, ProducerOptions options, ILogger<TokenProvider> logger)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _token;
    private DateTimeOffset _expiresAt;

    public async Task<string> GetTokenAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_token is not null && DateTimeOffset.UtcNow < _expiresAt) return _token;

            var response = await http.PostAsync($"{options.OidcBaseUrl}/connect/token",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = options.ClientId,
                    ["client_secret"] = options.ClientSecret,
                    ["scope"] = "logs:write"
                }), ct);
            response.EnsureSuccessStatusCode();

            var payload = await response.Content.ReadFromJsonAsync<TokenPayload>(cancellationToken: ct)
                ?? throw new InvalidOperationException("empty token response");
            _token = payload.AccessToken;
            _expiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(30, payload.ExpiresIn - 60));
            logger.LogDebug("Minted producer token, valid until {ExpiresAt:O}", _expiresAt);
            return _token;
        }
        finally
        {
            _gate.Release();
        }
    }

    private sealed record TokenPayload(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}
