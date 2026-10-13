using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LogPipe.Ingest.Api;

namespace LogPipe.Ingest.Tests;

[Collection("containers")]
public sealed class PipelineTests
{
    private readonly ContainersFixture _fixture;
    public PipelineTests(ContainersFixture fixture) => _fixture = fixture;

    private static async Task<string> WriteTokenAsync(HttpClient client)
    {
        var response = await client.PostAsync("/connect/token", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = "logpipe-writer",
                ["client_secret"] = "test-secret",
                ["scope"] = "logs:write"
            }));
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("access_token").GetString()!;
    }

    private static object Batch(string messageId, string? id = null) => new
    {
        entries = new[]
        {
            new { id, level = "Information", service = "pipeline-tests", message = messageId }
        }
    };

    [Fact]
    public async Task Rate_limited_request_returns_429()
    {
        using var factory = _fixture.CreateFactory(rateLimitPerMinute: 5);
        var client = factory.CreateClient();
        var token = await WriteTokenAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var first = await client.PostAsJsonAsync("/v1/logs", Batch("one"));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await client.PostAsJsonAsync("/v1/logs", Batch("two"));
        var third = await client.PostAsJsonAsync("/v1/logs", Batch("three"));
        var fourth = await client.PostAsJsonAsync("/v1/logs", Batch("four"));
        var fifth = await client.PostAsJsonAsync("/v1/logs", Batch("five"));
        var sixth = await client.PostAsJsonAsync("/v1/logs", Batch("six"));

        Assert.Equal(HttpStatusCode.OK, fifth.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, sixth.StatusCode);
        var body = await sixth.Content.ReadAsStringAsync();
        Assert.Contains("rate_limit_exceeded", body);
    }

    [Fact]
    public async Task Duplicate_id_in_same_batch_is_rejected()
    {
        using var factory = _fixture.CreateFactory();
        var client = factory.CreateClient();
        var token = await WriteTokenAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var id = Guid.NewGuid().ToString();

        var response = await client.PostAsJsonAsync("/v1/logs", new
        {
            entries = new[]
            {
                new { id, level = "Information", service = "pipeline-tests", message = "same" },
                new { id, level = "Information", service = "pipeline-tests", message = "same" }
            }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<IngestLogsResponse>();
        Assert.Equal(1, payload!.Accepted);
        Assert.Equal(1, payload.Rejected);
    }

    [Fact]
    public async Task Same_content_without_id_across_requests_is_deduplicated()
    {
        using var factory = _fixture.CreateFactory();
        var client = factory.CreateClient();
        var token = await WriteTokenAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var body = new
        {
            entries = new[]
            {
                new { level = "Warning", service = "pipeline-tests", message = "identical", timestamp = "2026-10-12T10:00:00.000Z" }
            }
        };

        var first = await client.PostAsJsonAsync("/v1/logs", body);
        var second = await client.PostAsJsonAsync("/v1/logs", body);

        var firstPayload = await first.Content.ReadFromJsonAsync<IngestLogsResponse>();
        var secondPayload = await second.Content.ReadFromJsonAsync<IngestLogsResponse>();
        Assert.Equal(1, firstPayload!.Accepted);
        Assert.Equal(0, firstPayload.Rejected);
        Assert.Equal(0, secondPayload!.Accepted);
        Assert.Equal(1, secondPayload.Rejected);
    }
}
