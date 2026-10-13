using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace LogPipe.Ingest.Api;

public static class OpenIddictSeed
{
    public const string WriterClientId = "logpipe-writer";
    public const string ReaderClientId = "logpipe-reader";

    public static async Task EnsureSeededAsync(IServiceProvider services)
    {
        var config = services.GetRequiredService<IConfiguration>();
        var environment = services.GetRequiredService<IHostEnvironment>();
        var clientSecret = config["Oidc:ClientSecret"];

        if (!environment.IsDevelopment() && string.IsNullOrWhiteSpace(clientSecret))
        {
            throw new InvalidOperationException(
                "Oidc:ClientSecret must be configured (env Oidc__ClientSecret). " +
                "An empty or whitespace-only secret is not allowed outside the Development environment.");
        }

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Persistence.OidcDbContext>();
        await db.Database.EnsureCreatedAsync();

        var applicationManager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var scopeManager = scope.ServiceProvider.GetRequiredService<IOpenIddictScopeManager>();

        foreach (var name in new[] { "logs:read", "logs:write" })
        {
            if (await scopeManager.FindByNameAsync(name) is null)
                await scopeManager.CreateAsync(new OpenIddictScopeDescriptor { Name = name, DisplayName = name });
        }

        if (string.IsNullOrWhiteSpace(clientSecret)) return; // Development without secrets: nothing to seed

        await EnsureClientAsync(applicationManager, WriterClientId, clientSecret, "logs:write");
        await EnsureClientAsync(applicationManager, ReaderClientId, clientSecret, "logs:read");
    }

    private static async Task EnsureClientAsync(
        IOpenIddictApplicationManager applicationManager,
        string clientId,
        string clientSecret,
        params string[] scopes)
    {
        var application = await applicationManager.FindByClientIdAsync(clientId);
        if (application is null)
        {
            var descriptor = new OpenIddictApplicationDescriptor
            {
                ClientId = clientId,
                ClientSecret = clientSecret,
                ClientType = ClientTypes.Confidential,
                Permissions =
                {
                    Permissions.Endpoints.Token,
                    Permissions.GrantTypes.ClientCredentials
                }
            };
            foreach (var scope in scopes)
                descriptor.Permissions.Add(Permissions.Prefixes.Scope + scope);

            await applicationManager.CreateAsync(descriptor);
        }
        else if (!await applicationManager.ValidateClientSecretAsync(application, clientSecret))
        {
            await applicationManager.UpdateAsync(application, clientSecret);
        }
    }
}
