using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace LogPipe.Ingest.Tests;

[Collection("containers")]
public sealed class AuthTests
{
    private readonly ContainersFixture _fixture;
    public AuthTests(ContainersFixture fixture) => _fixture = fixture;

    private static async Task<string> GetTokenAsync(HttpClient client, string scope)
    {
        var clientId = scope == "logs:read" ? "logpipe-reader" : "logpipe-writer";
        var response = await client.PostAsync("/connect/token", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = clientId,
                ["client_secret"] = "test-secret",
                ["scope"] = scope
            }));
        response.EnsureSuccessStatusCode();
        using var doc = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("access_token").GetString()!;
    }

    private static object ValidBatch(int count = 1) => new
    {
        entries = Enumerable.Range(0, count).Select(i => new
        {
            level = "Information",
            service = "auth-tests",
            message = $"hello {i}"
        }).ToArray()
    };

    [Fact]
    public async Task Missing_token_returns_401()
    {
        using var factory = _fixture.CreateFactory();
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/v1/logs", ValidBatch());
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Reader_token_cannot_write_returns_403()
    {
        using var factory = _fixture.CreateFactory();
        var client = factory.CreateClient();
        var token = await GetTokenAsync(client, "logs:read");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.PostAsJsonAsync("/v1/logs", ValidBatch());
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Writer_token_accepted_returns_200()
    {
        using var factory = _fixture.CreateFactory();
        var client = factory.CreateClient();
        var token = await GetTokenAsync(client, "logs:write");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.PostAsJsonAsync("/v1/logs", ValidBatch(3));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<Api.IngestLogsResponse>();
        Assert.Equal(3, payload!.Accepted);
        Assert.Equal(0, payload.Rejected);
    }

    [Fact]
    public async Task Invalid_entry_returns_400_with_reason()
    {
        using var factory = _fixture.CreateFactory();
        var client = factory.CreateClient();
        var token = await GetTokenAsync(client, "logs:write");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.PostAsJsonAsync("/v1/logs", new
        {
            entries = new[] { new { level = "Bogus", service = "s", message = "m" } }
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("unknown level", body);
    }

    [Fact]
    public async Task More_than_max_entries_returns_400()
    {
        using var factory = _fixture.CreateFactory();
        var client = factory.CreateClient();
        var token = await GetTokenAsync(client, "logs:write");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.PostAsJsonAsync("/v1/logs", ValidBatch(1001));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Empty_entries_returns_400()
    {
        using var factory = _fixture.CreateFactory();
        var client = factory.CreateClient();
        var token = await GetTokenAsync(client, "logs:write");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.PostAsJsonAsync("/v1/logs", new { entries = Array.Empty<object>() });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
