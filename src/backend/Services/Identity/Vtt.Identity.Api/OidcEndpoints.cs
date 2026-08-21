using System.Security.Claims;
using Microsoft.AspNetCore;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using Vtt.Identity.Application;

namespace Vtt.Identity.Api;

internal static class OidcEndpoints
{
    public static IEndpointRouteBuilder MapVttOidcEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapMethods("/connect/authorize", [HttpMethods.Get, HttpMethods.Post], AuthorizeAsync);
        endpoints.MapGet("/connect/userinfo", (ClaimsPrincipal principal) => Results.Ok(new
        {
            sub = principal.GetClaim(OpenIddictConstants.Claims.Subject),
            name = principal.GetClaim(OpenIddictConstants.Claims.Name),
        })).RequireAuthorization();
        return endpoints;
    }

    private static async Task<IResult> AuthorizeAsync(
        HttpContext context,
        IdentityAccessService identityAccess,
        CancellationToken cancellationToken)
    {
        var request = context.GetOpenIddictServerRequest();
        if (request is null)
        {
            return Results.BadRequest();
        }

        var sessionToken = context.Request.Cookies["vtt.session"] ?? string.Empty;
        var user = await identityAccess.GetMeAsync(sessionToken, cancellationToken);
        if (user is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: OpenIddictConstants.Errors.LoginRequired,
                detail: "Authenticate through the VTT BFF before starting authorization.");
        }

        var identity = new ClaimsIdentity(
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
            OpenIddictConstants.Claims.Name,
            OpenIddictConstants.Claims.Role);
        identity.SetClaim(OpenIddictConstants.Claims.Subject, user.UserId.ToString("D"));
        identity.SetClaim(OpenIddictConstants.Claims.Name, user.DisplayName);
        var principal = new ClaimsPrincipal(identity);
        principal.SetScopes(request.GetScopes());
        principal.SetResources("vtt-api");
        principal.SetDestinations(claim => claim.Type switch
        {
            OpenIddictConstants.Claims.Name =>
            [OpenIddictConstants.Destinations.AccessToken, OpenIddictConstants.Destinations.IdentityToken],
            _ => [OpenIddictConstants.Destinations.AccessToken],
        });

        return Results.SignIn(
            principal,
            authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }
}
