using System.Security.Claims;
using LogPipe.Contracts.Validation;
using LogPipe.Ingest.Auth;
using LogPipe.Ingest.Pipeline;
using Microsoft.AspNetCore.Authorization;

namespace LogPipe.Ingest.Api;

public sealed record LogEntryDto(
    string? Id, string? Timestamp, string? Level, string? Service,
    string? Message, string? TraceId, string? SpanId, Dictionary<string, string>? Attributes);

public sealed record IngestLogsRequest(IReadOnlyList<LogEntryDto>? Entries);

public sealed record IngestLogsResponse(int Accepted, int Rejected);

public static class LogEndpoints
{
    public static IEndpointRouteBuilder MapLogEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/logs", HandleAsync)
            .WithName("IngestLogs")
            .RequireAuthorization(ScopePolicies.LogsWrite)
            .Produces<IngestLogsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
        return app;
    }

    private static async Task<IResult> HandleAsync(
        IngestLogsRequest request,
        LogIngestService service,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var entries = request.Entries;
        if (entries is null || entries.Count == 0 || entries.Count > LogEntryRules.MaxEntriesPerBatch)
        {
            return Results.Problem(
                $"entries must be 1..{LogEntryRules.MaxEntriesPerBatch}",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var clientId = user.FindFirstValue("client_id") ?? "unknown";
        var outcome = await service.IngestAsync(clientId, entries, ct);

        if (outcome.ValidationFailure is not null)
        {
            return Results.Problem(outcome.ValidationFailure, statusCode: StatusCodes.Status400BadRequest);
        }

        if (outcome.RateLimited)
        {
            return Results.Json(new { error = "rate_limit_exceeded" },
                statusCode: StatusCodes.Status429TooManyRequests);
        }

        return Results.Ok(new IngestLogsResponse(outcome.Accepted, outcome.Rejected));
    }
}
