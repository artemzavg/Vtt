using Microsoft.AspNetCore.Mvc;
using Vtt.Identity.Application;
using Vtt.Identity.Contracts;
using Vtt.Identity.Domain;

namespace Vtt.Identity.Api;

internal static class IdentityEndpoints
{
    private const string SessionHeader = "X-Vtt-Session";
    private const string CsrfHeader = "X-CSRF-Token";

    public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var publicApi = endpoints.MapGroup("/api/v1/auth").RequireRateLimiting("auth");
        publicApi.MapPost("/registrations", RegisterAsync);
        publicApi.MapPost("/email-verifications", VerifyEmailAsync);
        publicApi.MapPost("/password-resets", RequestResetAsync);
        publicApi.MapPost("/password-resets/complete", CompleteResetAsync);

        var internalApi = endpoints.MapGroup("/internal/v1/auth");
        internalApi.MapPost("/sessions", LoginAsync).RequireRateLimiting("auth");
        internalApi.MapPost("/sessions/refresh", RefreshAsync).RequireRateLimiting("auth");
        internalApi.MapDelete("/sessions/current", LogoutAsync);
        internalApi.MapGet("/me", GetMeAsync);
        internalApi.MapPut("/me", UpdateProfileAsync);
        internalApi.MapGet("/sessions", ListSessionsAsync);
        internalApi.MapDelete("/sessions/{sessionId:guid}", RevokeSessionAsync);
        internalApi.MapDelete("/sessions", RevokeOtherSessionsAsync);
        return endpoints;
    }

    private static async Task<IResult> RegisterAsync(
        RegisterUserRequest request,
        IdentityAccessService service,
        CancellationToken cancellationToken) =>
        await ExecutePublicAsync(async () =>
        {
            var result = await service.RegisterAsync(
                new RegistrationCommand(
                    request.Email,
                    request.Password,
                    request.DisplayName,
                    request.Locale,
                    request.TimeZone,
                    request.TermsVersion,
                    request.PrivacyVersion),
                cancellationToken);
            return Results.Accepted(value: new GenericMessageResponse(result.Message));
        }, cancellationToken);

    private static async Task<IResult> VerifyEmailAsync(
        ChallengeTokenRequest request,
        IdentityAccessService service,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(async () =>
            await service.VerifyEmailAsync(request.ChallengeToken, cancellationToken)
                ? Results.NoContent()
                : Problem(StatusCodes.Status400BadRequest, "identity.challenge_invalid", "Ссылка недействительна или устарела."));

    private static async Task<IResult> RequestResetAsync(
        PasswordResetRequest request,
        IdentityAccessService service,
        CancellationToken cancellationToken) =>
        await ExecutePublicAsync(async () =>
        {
            var result = await service.RequestPasswordResetAsync(request.Email, cancellationToken);
            return Results.Accepted(value: new GenericMessageResponse(result.Message));
        }, cancellationToken);

    private static async Task<IResult> CompleteResetAsync(
        PasswordResetCompleteRequest request,
        IdentityAccessService service,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(async () =>
            await service.CompletePasswordResetAsync(
                request.ChallengeToken,
                request.NewPassword,
                cancellationToken)
                ? Results.NoContent()
                : Problem(StatusCodes.Status400BadRequest, "identity.challenge_invalid", "Ссылка недействительна или устарела."));

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        HttpContext context,
        IdentityAccessService service,
        CancellationToken cancellationToken) =>
        await ExecutePublicAsync(async () =>
        {
            var device = string.IsNullOrWhiteSpace(request.DeviceLabel)
                ? context.Request.Headers.UserAgent.ToString()
                : request.DeviceLabel;
            var result = await service.LoginAsync(
                request.Email,
                request.Password,
                device,
                cancellationToken);
            return result.Outcome == LoginOutcome.Succeeded && result.Session is not null
                ? Results.Ok(ToInternalSession(result.Session))
                : Problem(StatusCodes.Status401Unauthorized, "identity.credentials_invalid", "Неверный email или пароль.");
        }, cancellationToken);

    private static async Task<IResult> RefreshAsync(
        HttpContext context,
        IdentityAccessService service,
        CancellationToken cancellationToken)
    {
        var result = await service.RefreshAsync(GetHeader(context, SessionHeader), cancellationToken);
        if (result.Outcome == RefreshOutcome.Rotated && result.Session is not null)
        {
            return Results.Ok(ToInternalSession(result.Session));
        }

        var status = result.Outcome == RefreshOutcome.BenignRace
            ? StatusCodes.Status409Conflict
            : StatusCodes.Status401Unauthorized;
        return Problem(status, $"identity.refresh_{result.Outcome.ToString().ToLowerInvariant()}",
            result.Outcome == RefreshOutcome.BenignRace
                ? "Сессия уже обновлена параллельным запросом."
                : "Сессия недействительна.");
    }

    private static async Task<IResult> GetMeAsync(
        HttpContext context,
        IdentityAccessService service,
        CancellationToken cancellationToken)
    {
        var user = await service.GetMeAsync(GetHeader(context, SessionHeader), cancellationToken);
        return user is null ? Unauthorized() : Results.Ok(ToProfile(user));
    }

    private static async Task<IResult> UpdateProfileAsync(
        UpdateProfileRequest request,
        HttpContext context,
        IdentityAccessService service,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(async () =>
        {
            if (!TryGetExpectedVersion(context, out var expectedVersion))
            {
                return Problem(StatusCodes.Status428PreconditionRequired, "identity.if_match_required", "Передайте версию профиля в If-Match.");
            }

            var user = await service.UpdateProfileAsync(
                GetHeader(context, SessionHeader),
                GetHeader(context, CsrfHeader),
                new ProfileUpdateCommand(request.DisplayName, request.Locale, request.TimeZone, expectedVersion),
                cancellationToken);
            return user is null ? Unauthorized() : Results.Ok(ToProfile(user));
        });

    private static async Task<IResult> ListSessionsAsync(
        HttpContext context,
        IdentityAccessService service,
        CancellationToken cancellationToken)
    {
        var sessions = await service.ListSessionsAsync(GetHeader(context, SessionHeader), cancellationToken);
        return sessions is null
            ? Unauthorized()
            : Results.Ok(sessions.Select(session => new LoginSessionResponse(
                session.SessionId,
                session.DeviceLabel,
                session.CreatedAt,
                session.LastUsedAt,
                session.ExpiresAt,
                session.IsCurrent,
                session.IsRevoked)));
    }

    private static async Task<IResult> RevokeSessionAsync(
        Guid sessionId,
        HttpContext context,
        IdentityAccessService service,
        CancellationToken cancellationToken) =>
        await service.RevokeSessionAsync(
            GetHeader(context, SessionHeader),
            GetHeader(context, CsrfHeader),
            sessionId,
            cancellationToken)
            ? Results.NoContent()
            : Unauthorized();

    private static async Task<IResult> RevokeOtherSessionsAsync(
        HttpContext context,
        IdentityAccessService service,
        CancellationToken cancellationToken) =>
        await service.RevokeOtherSessionsAsync(
            GetHeader(context, SessionHeader),
            GetHeader(context, CsrfHeader),
            cancellationToken)
            ? Results.NoContent()
            : Unauthorized();

    private static async Task<IResult> LogoutAsync(
        HttpContext context,
        IdentityAccessService service,
        CancellationToken cancellationToken) =>
        await service.LogoutAsync(
            GetHeader(context, SessionHeader),
            GetHeader(context, CsrfHeader),
            cancellationToken)
            ? Results.NoContent()
            : Unauthorized();

    private static InternalSessionResponse ToInternalSession(IssuedSession session) =>
        new(session.SessionToken, session.CsrfToken, session.ExpiresAt, ToProfile(session.User));

    private static UserProfileResponse ToProfile(UserSummary user) =>
        new(user.UserId, user.DisplayName, user.Locale, user.TimeZone, user.Version, user.SecurityRevision);

    private static string GetHeader(HttpContext context, string name) =>
        context.Request.Headers[name].ToString();

    private static bool TryGetExpectedVersion(HttpContext context, out long version) =>
        long.TryParse(context.Request.Headers.IfMatch.ToString().Trim('"'), out version);

    private static IResult Unauthorized() =>
        Problem(StatusCodes.Status401Unauthorized, "identity.session_invalid", "Требуется действующая сессия.");

    private static IResult Problem(int status, string code, string detail) =>
        Results.Problem(statusCode: status, title: code, detail: detail,
            extensions: new Dictionary<string, object?> { ["code"] = code });

    private static async Task<IResult> ExecuteAsync(Func<Task<IResult>> operation)
    {
        try
        {
            return await operation();
        }
        catch (PasswordPolicyException)
        {
            return Problem(StatusCodes.Status400BadRequest, "identity.password_policy", "Пароль не соответствует политике безопасности.");
        }
        catch (AccountVersionConflictException)
        {
            return Problem(StatusCodes.Status409Conflict, "identity.version_conflict", "Профиль был изменён в другой сессии.");
        }
        catch (ArgumentException exception)
        {
            return Problem(StatusCodes.Status400BadRequest, "identity.validation_failed", exception.Message);
        }
    }

    private static async Task<IResult> ExecutePublicAsync(
        Func<Task<IResult>> operation,
        CancellationToken cancellationToken)
    {
        var startedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            return await ExecuteAsync(operation);
        }
        finally
        {
            var remaining = TimeSpan.FromMilliseconds(200) -
                System.Diagnostics.Stopwatch.GetElapsedTime(startedAt);
            if (remaining > TimeSpan.Zero)
            {
                await Task.Delay(remaining, cancellationToken);
            }
        }
    }
}
