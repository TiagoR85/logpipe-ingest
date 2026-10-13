using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace LogPipe.Ingest.Auth;

public static class ScopePolicies
{
    public const string LogsWrite = "LogsWrite";
    public const string LogsRead = "LogsRead";

    public static void AddLogPipePolicies(this AuthorizationOptions options)
    {
        options.AddPolicy(LogsWrite,
            policy => policy.RequireAssertion(ctx => HasScope(ctx.User, "logs:write")));
        options.AddPolicy(LogsRead,
            policy => policy.RequireAssertion(ctx => HasScope(ctx.User, "logs:read")));
    }

    public static bool HasScope(ClaimsPrincipal principal, string scope)
    {
        var scopes = principal.FindAll("scope")
            .Concat(principal.FindAll("scp"))
            .SelectMany(c => c.Value.Split(' ',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return scopes.Contains(scope, StringComparer.Ordinal);
    }
}
