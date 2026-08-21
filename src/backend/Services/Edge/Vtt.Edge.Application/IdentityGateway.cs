namespace Vtt.Edge.Application;

public sealed record RegistrationInput(
    string Email,
    string Password,
    string DisplayName,
    string Locale,
    string TimeZone,
    string TermsVersion,
    string PrivacyVersion);

public sealed record LoginInput(string Email, string Password, string? DeviceLabel);

public sealed record ProfileInput(string DisplayName, string Locale, string TimeZone);

public sealed record BffUser(
    Guid UserId,
    string DisplayName,
    string Locale,
    string TimeZone,
    long Version,
    long SecurityRevision);

public sealed record GatewaySession(
    string SessionToken,
    string CsrfToken,
    DateTimeOffset ExpiresAt,
    BffUser User);

public sealed record GatewayLoginSession(
    Guid SessionId,
    string DeviceLabel,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastUsedAt,
    DateTimeOffset ExpiresAt,
    bool IsCurrent,
    bool IsRevoked);

public sealed record GatewayResponse<T>(int StatusCode, T? Value, string? ProblemCode = null)
{
    public bool IsSuccess => StatusCode is >= 200 and < 300;
}

public interface IIdentityGateway
{
    Task<GatewayResponse<string>> RegisterAsync(RegistrationInput input, CancellationToken cancellationToken);
    Task<GatewayResponse<string>> VerifyEmailAsync(string token, CancellationToken cancellationToken);
    Task<GatewayResponse<string>> RequestResetAsync(string email, CancellationToken cancellationToken);
    Task<GatewayResponse<string>> CompleteResetAsync(string token, string password, CancellationToken cancellationToken);
    Task<GatewayResponse<GatewaySession>> LoginAsync(LoginInput input, CancellationToken cancellationToken);
    Task<GatewayResponse<GatewaySession>> RefreshAsync(string sessionToken, CancellationToken cancellationToken);
    Task<GatewayResponse<BffUser>> GetMeAsync(string sessionToken, CancellationToken cancellationToken);
    Task<GatewayResponse<BffUser>> UpdateProfileAsync(string sessionToken, string csrfToken, long version, ProfileInput input, CancellationToken cancellationToken);
    Task<GatewayResponse<IReadOnlyList<GatewayLoginSession>>> ListSessionsAsync(string sessionToken, CancellationToken cancellationToken);
    Task<GatewayResponse<string>> RevokeSessionAsync(string sessionToken, string csrfToken, Guid sessionId, CancellationToken cancellationToken);
    Task<GatewayResponse<string>> RevokeOtherSessionsAsync(string sessionToken, string csrfToken, CancellationToken cancellationToken);
    Task<GatewayResponse<string>> LogoutAsync(string sessionToken, string csrfToken, CancellationToken cancellationToken);
}
