using System.Security.Cryptography;
using System.Text;
using Vtt.Edge.Application;
using Vtt.Edge.Contracts;

namespace Vtt.Edge.Api;

internal static class IdentityBffEndpoints
{
    internal const string SessionCookie = "vtt.session";
    internal const string CsrfCookie = "vtt.csrf";
    internal const string CsrfHeader = "X-CSRF-Token";

    public static IEndpointRouteBuilder MapIdentityBff(this IEndpointRouteBuilder endpoints)
    {
        var auth = endpoints.MapGroup("/v1/auth");
        auth.MapPost("/registrations", RegisterAsync).RequireRateLimiting("auth");
        auth.MapPost("/email-verifications", VerifyAsync).RequireRateLimiting("auth");
        auth.MapPost("/password-resets", RequestResetAsync).RequireRateLimiting("auth");
        auth.MapPost("/password-resets/complete", CompleteResetAsync).RequireRateLimiting("auth");
        auth.MapPost("/login", LoginAsync).RequireRateLimiting("auth");
        auth.MapGet("/oidc/start", EdgeOidc.StartAsync).RequireRateLimiting("auth");
        auth.MapPost("/refresh", RefreshAsync).RequireRateLimiting("auth");
        auth.MapPost("/logout", LogoutAsync);

        endpoints.MapGet("/v1/me", GetMeAsync);
        endpoints.MapPut("/v1/me", UpdateProfileAsync);
        endpoints.MapGet("/v1/me/sessions", ListSessionsAsync);
        endpoints.MapDelete("/v1/me/sessions/{sessionId:guid}", RevokeSessionAsync);
        endpoints.MapDelete("/v1/me/sessions", RevokeOtherSessionsAsync);
        return endpoints;
    }

    private static async Task<IResult> RegisterAsync(RegisterRequest request, IIdentityGateway gateway, CancellationToken ct) =>
        ToResult(await gateway.RegisterAsync(new RegistrationInput(
            request.Email, request.Password, request.DisplayName, request.Locale, request.TimeZone,
            request.TermsVersion, request.PrivacyVersion), ct));

    private static async Task<IResult> VerifyAsync(TokenRequest request, IIdentityGateway gateway, CancellationToken ct) =>
        ToResult(await gateway.VerifyEmailAsync(request.ChallengeToken, ct));

    private static async Task<IResult> RequestResetAsync(ResetRequest request, IIdentityGateway gateway, CancellationToken ct) =>
        ToResult(await gateway.RequestResetAsync(request.Email, ct));

    private static async Task<IResult> CompleteResetAsync(ResetCompleteRequest request, IIdentityGateway gateway, CancellationToken ct) =>
        ToResult(await gateway.CompleteResetAsync(request.ChallengeToken, request.NewPassword, ct));

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        HttpContext context,
        IIdentityGateway gateway,
        CancellationToken ct)
    {
        var result = await gateway.LoginAsync(new LoginInput(request.Email, request.Password, request.DeviceLabel), ct);
        if (!result.IsSuccess || result.Value is null)
        {
            return ToResult(result);
        }

        WriteSessionCookies(context, result.Value);
        return Results.Ok(new SessionResponse(result.Value.ExpiresAt, ToUser(result.Value.User)));
    }

    private static async Task<IResult> RefreshAsync(HttpContext context, IIdentityGateway gateway, CancellationToken ct)
    {
        if (!TryGetSession(context, requireCsrf: true, out var session, out _))
        {
            return Forbidden("edge.csrf_invalid");
        }

        var result = await gateway.RefreshAsync(session, ct);
        if (!result.IsSuccess || result.Value is null)
        {
            if (result.ProblemCode == "identity.refresh_benignrace")
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: result.ProblemCode,
                    extensions: new Dictionary<string, object?> { ["code"] = result.ProblemCode });
            }

            ClearCookies(context);
            return ToResult(result);
        }

        WriteSessionCookies(context, result.Value);
        return Results.Ok(new SessionResponse(result.Value.ExpiresAt, ToUser(result.Value.User)));
    }

    private static async Task<IResult> LogoutAsync(HttpContext context, IIdentityGateway gateway, CancellationToken ct)
    {
        if (!TryGetSession(context, requireCsrf: true, out var session, out var csrf))
        {
            return Forbidden("edge.csrf_invalid");
        }

        var result = await gateway.LogoutAsync(session, csrf, ct);
        ClearCookies(context);
        return ToResult(result);
    }

    private static async Task<IResult> GetMeAsync(HttpContext context, IIdentityGateway gateway, CancellationToken ct)
    {
        if (!TryGetSession(context, requireCsrf: false, out var session, out _))
        {
            return Unauthorized("edge.session_missing");
        }

        var result = await gateway.GetMeAsync(session, ct);
        return result.IsSuccess && result.Value is not null
            ? Results.Ok(ToUser(result.Value))
            : ToResult(result);
    }

    private static async Task<IResult> UpdateProfileAsync(UpdateProfileRequest request, HttpContext context, IIdentityGateway gateway, CancellationToken ct)
    {
        if (!TryGetSession(context, requireCsrf: true, out var session, out var csrf))
        {
            return Forbidden("edge.csrf_invalid");
        }

        var result = await gateway.UpdateProfileAsync(session, csrf, request.Version,
            new ProfileInput(request.DisplayName, request.Locale, request.TimeZone), ct);
        return result.IsSuccess && result.Value is not null
            ? Results.Ok(ToUser(result.Value))
            : ToResult(result);
    }

    private static async Task<IResult> ListSessionsAsync(HttpContext context, IIdentityGateway gateway, CancellationToken ct)
    {
        if (!TryGetSession(context, requireCsrf: false, out var session, out _))
        {
            return Unauthorized("edge.session_missing");
        }

        return ToResult(await gateway.ListSessionsAsync(session, ct));
    }

    private static async Task<IResult> RevokeSessionAsync(Guid sessionId, HttpContext context, IIdentityGateway gateway, CancellationToken ct)
    {
        if (!TryGetSession(context, requireCsrf: true, out var session, out var csrf))
        {
            return Forbidden("edge.csrf_invalid");
        }

        var result = await gateway.RevokeSessionAsync(session, csrf, sessionId, ct);
        return ToResult(result);
    }

    private static async Task<IResult> RevokeOtherSessionsAsync(HttpContext context, IIdentityGateway gateway, CancellationToken ct)
    {
        if (!TryGetSession(context, requireCsrf: true, out var session, out var csrf))
        {
            return Forbidden("edge.csrf_invalid");
        }

        return ToResult(await gateway.RevokeOtherSessionsAsync(session, csrf, ct));
    }

    internal static bool HasValidCsrf(HttpContext context)
    {
        var csrf = context.Request.Headers[CsrfHeader].ToString();
        var csrfCookie = context.Request.Cookies[CsrfCookie] ?? string.Empty;
        return csrf.Length > 0 && csrfCookie.Length == csrf.Length &&
            CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(csrf), Encoding.UTF8.GetBytes(csrfCookie));
    }

    private static bool TryGetSession(HttpContext context, bool requireCsrf, out string session, out string csrf)
    {
        session = context.Request.Cookies[SessionCookie] ?? string.Empty;
        csrf = context.Request.Headers[CsrfHeader].ToString();
        if (session.Length == 0)
        {
            return false;
        }

        if (!requireCsrf)
        {
            return true;
        }

        return HasValidCsrf(context);
    }

    private static void WriteSessionCookies(HttpContext context, GatewaySession session)
    {
        var secure = !context.RequestServices.GetRequiredService<IHostEnvironment>().IsDevelopment();
        context.Response.Cookies.Append(SessionCookie, session.SessionToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = secure,
            SameSite = SameSiteMode.Strict,
            Expires = session.ExpiresAt,
            Path = "/",
        });
        context.Response.Cookies.Append(CsrfCookie, session.CsrfToken, new CookieOptions
        {
            HttpOnly = false,
            Secure = secure,
            SameSite = SameSiteMode.Strict,
            Expires = session.ExpiresAt,
            Path = "/",
        });
        context.Response.Headers.CacheControl = "no-store";
    }

    private static void ClearCookies(HttpContext context)
    {
        context.Response.Cookies.Delete(SessionCookie, new CookieOptions { Path = "/" });
        context.Response.Cookies.Delete(CsrfCookie, new CookieOptions { Path = "/" });
    }

    private static UserResponse ToUser(BffUser user) =>
        new(user.UserId, user.DisplayName, user.Locale, user.TimeZone, user.Version);

    private static IResult ToResult<T>(GatewayResponse<T> result)
    {
        if (result.IsSuccess)
        {
            return result.StatusCode == StatusCodes.Status204NoContent
                ? Results.NoContent()
                : Results.Json(result.Value, statusCode: result.StatusCode);
        }

        var status = result.StatusCode is >= 400 and < 600
            ? result.StatusCode
            : StatusCodes.Status502BadGateway;
        var code = result.ProblemCode ?? "edge.identity_unavailable";
        return Results.Problem(statusCode: status, title: code,
            extensions: new Dictionary<string, object?> { ["code"] = code });
    }

    private static IResult Unauthorized(string code) =>
        Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: code,
            extensions: new Dictionary<string, object?> { ["code"] = code });

    private static IResult Forbidden(string code) =>
        Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: code,
            extensions: new Dictionary<string, object?> { ["code"] = code });
}
