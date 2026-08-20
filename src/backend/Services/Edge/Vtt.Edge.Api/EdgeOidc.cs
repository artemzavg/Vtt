using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Vtt.Edge.Application;

namespace Vtt.Edge.Api;

internal static class EdgeOidc
{
    internal const string Scheme = "vtt.oidc";
    private const string TemporaryScheme = "vtt.oidc.temporary";

    public static IServiceCollection AddEdgeOidc(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var authority = EnsureTrailingSlash(
            configuration["Identity:Oidc:Authority"] ?? "https://localhost:5101/");
        var backchannelAuthority = EnsureTrailingSlash(
            configuration["Identity:Oidc:BackchannelAuthority"] ?? authority);
        var callbackPath = configuration["Identity:Oidc:CallbackPath"] ?? "/signin-oidc";
        var publicWebBaseUrl = configuration["Identity:Oidc:PublicWebBaseUrl"]?.TrimEnd('/');

        services.AddAuthentication()
            .AddCookie(TemporaryScheme, options =>
            {
                options.Cookie.Name = "vtt.oidc.temporary";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = environment.IsDevelopment()
                    ? CookieSecurePolicy.None
                    : CookieSecurePolicy.Always;
                options.ExpireTimeSpan = TimeSpan.FromMinutes(5);
            })
            .AddOpenIdConnect(Scheme, options =>
            {
                options.Authority = authority;
                options.ClientId = configuration["Identity:Oidc:ClientId"] ?? "vtt-web-bff";
                options.CallbackPath = callbackPath;
                options.ResponseType = OpenIdConnectResponseType.Code;
                options.ResponseMode = OpenIdConnectResponseMode.Query;
                options.SignInScheme = TemporaryScheme;
                options.UsePkce = true;
                options.SaveTokens = false;
                options.GetClaimsFromUserInfoEndpoint = false;
                options.MapInboundClaims = false;
                options.RequireHttpsMetadata = !environment.IsDevelopment();
                options.Scope.Clear();
                options.Scope.Add("openid");
                options.Scope.Add("profile");
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    NameClaimType = "name",
                    ValidateAudience = true,
                    ValidAudience = options.ClientId,
                    ValidateIssuer = true,
                    ValidIssuer = authority,
                };
                options.BackchannelHttpHandler = new AuthorityRewriteHandler(
                    new Uri(authority),
                    new Uri(backchannelAuthority),
                    new SocketsHttpHandler());
                options.CorrelationCookie.HttpOnly = true;
                options.CorrelationCookie.SameSite = SameSiteMode.Lax;
                options.NonceCookie.HttpOnly = true;
                options.NonceCookie.SameSite = SameSiteMode.Lax;
                if (environment.IsDevelopment())
                {
                    options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.None;
                    options.NonceCookie.SecurePolicy = CookieSecurePolicy.None;
                }

                options.Events = new OpenIdConnectEvents
                {
                    OnTokenValidated = ValidateBffSessionAsync,
                    OnTicketReceived = context =>
                    {
                        context.HandleResponse();
                        var returnUrl = NormalizeReturnUrl(context.Properties?.RedirectUri);
                        context.Response.Redirect(publicWebBaseUrl is null ? returnUrl : publicWebBaseUrl + returnUrl);
                        return Task.CompletedTask;
                    },
                    OnRemoteFailure = context =>
                    {
                        context.HandleResponse();
                        context.Response.Redirect("/auth?oidcError=callback_failed");
                        return Task.CompletedTask;
                    },
                };
            });

        return services;
    }

    public static async Task<IResult> StartAsync(
        string? returnUrl,
        HttpContext context,
        IIdentityGateway gateway,
        CancellationToken cancellationToken)
    {
        var sessionToken = context.Request.Cookies[IdentityBffEndpoints.SessionCookie];
        if (string.IsNullOrWhiteSpace(sessionToken))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "edge.session_missing",
                extensions: new Dictionary<string, object?> { ["code"] = "edge.session_missing" });
        }

        var session = await gateway.GetMeAsync(sessionToken, cancellationToken);
        if (!session.IsSuccess)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "edge.session_invalid",
                extensions: new Dictionary<string, object?> { ["code"] = "edge.session_invalid" });
        }

        return Results.Challenge(
            new AuthenticationProperties { RedirectUri = NormalizeReturnUrl(returnUrl) },
            [Scheme]);
    }

    internal static string NormalizeReturnUrl(string? returnUrl) =>
        !string.IsNullOrWhiteSpace(returnUrl) &&
        returnUrl.StartsWith('/') &&
        !returnUrl.StartsWith("//", StringComparison.Ordinal) &&
        !returnUrl.Contains('\\') &&
        !returnUrl.Any(char.IsControl)
            ? returnUrl
            : "/account";

    private static async Task ValidateBffSessionAsync(TokenValidatedContext context)
    {
        var sessionToken = context.HttpContext.Request.Cookies[IdentityBffEndpoints.SessionCookie];
        var subject = context.Principal?.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(sessionToken) || !Guid.TryParse(subject, out var subjectId))
        {
            context.Fail("The OIDC response is not bound to a valid BFF session.");
            return;
        }

        var gateway = context.HttpContext.RequestServices.GetRequiredService<IIdentityGateway>();
        var session = await gateway.GetMeAsync(sessionToken, context.HttpContext.RequestAborted);
        if (!session.IsSuccess || session.Value?.UserId != subjectId)
        {
            context.Fail("The OIDC subject does not match the active BFF session.");
        }
    }

    private static string EnsureTrailingSlash(string value) => value.EndsWith('/') ? value : value + "/";
}

internal sealed class AuthorityRewriteHandler(
    Uri publicAuthority,
    Uri backchannelAuthority,
    HttpMessageHandler innerHandler) : DelegatingHandler(innerHandler)
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var isDiscoveryRequest = request.RequestUri?.AbsolutePath.EndsWith(
            "/.well-known/openid-configuration",
            StringComparison.Ordinal) == true;
        if (request.RequestUri is { } requestUri &&
            publicAuthority.IsBaseOf(requestUri))
        {
            request.RequestUri = new Uri(
                backchannelAuthority,
                publicAuthority.MakeRelativeUri(requestUri));
        }

        var response = await base.SendAsync(request, cancellationToken);
        if (isDiscoveryRequest && response.Content is not null)
        {
            var contentType = response.Content.Headers.ContentType;
            var document = await response.Content.ReadAsStringAsync(cancellationToken);
            document = document.Replace(
                backchannelAuthority.AbsoluteUri.TrimEnd('/'),
                publicAuthority.AbsoluteUri.TrimEnd('/'),
                StringComparison.Ordinal);
            response.Content = new StringContent(document, Encoding.UTF8);
            response.Content.Headers.ContentType = contentType;
        }

        return response;
    }
}
