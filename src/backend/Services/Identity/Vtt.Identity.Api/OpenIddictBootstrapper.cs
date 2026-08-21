using OpenIddict.Abstractions;

namespace Vtt.Identity.Api;

internal static class OpenIddictBootstrapper
{
    public static async Task EnsureWebClientAsync(IServiceProvider services, IConfiguration configuration, CancellationToken cancellationToken)
    {
        var manager = services.GetRequiredService<IOpenIddictApplicationManager>();
        const string clientId = "vtt-web-bff";
        var redirectUri = configuration["Identity:Oidc:WebRedirectUri"] ?? "https://localhost/signin-oidc";
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = clientId,
            ClientType = OpenIddictConstants.ClientTypes.Public,
            ConsentType = OpenIddictConstants.ConsentTypes.Implicit,
            DisplayName = "VTT Web BFF",
            RedirectUris = { new Uri(redirectUri, UriKind.Absolute) },
            Permissions =
            {
                OpenIddictConstants.Permissions.Endpoints.Authorization,
                OpenIddictConstants.Permissions.Endpoints.Token,
                OpenIddictConstants.Permissions.Endpoints.Revocation,
                OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode,
                OpenIddictConstants.Permissions.ResponseTypes.Code,
                OpenIddictConstants.Permissions.Scopes.Email,
                OpenIddictConstants.Permissions.Scopes.Profile,
            },
            Requirements =
            {
                OpenIddictConstants.Requirements.Features.ProofKeyForCodeExchange,
            },
        };
        var application = await manager.FindByClientIdAsync(clientId, cancellationToken);
        if (application is null)
        {
            await manager.CreateAsync(descriptor, cancellationToken);
        }
        else
        {
            await manager.UpdateAsync(application, descriptor, cancellationToken);
        }
    }
}
