using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace LogPipe.SampleProducer;

public sealed class BatchLogSender(
    HttpClient http, TokenProvider tokens, ProducerOptions options, ILogger<BatchLogSender> logger)
{
    public async Task<int> SendAsync(IReadOnlyList<ProducerLogEntry> batch, CancellationToken ct)
    {
        var token = await tokens.GetTokenAsync(ct);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{options.IngestBaseUrl}/v1/logs");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new { entries = batch });

        var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            logger.LogWarning("Ingest returned {Status}: {Body}", response.StatusCode, body);
            return 0;
        }

        var result = await response.Content.ReadFromJsonAsync<SendResult>(cancellationToken: ct);
        return result?.Accepted ?? 0;
    }

    private sealed record SendResult(
        [property: JsonPropertyName("accepted")] int Accepted,
        [property: JsonPropertyName("rejected")] int Rejected);
}
