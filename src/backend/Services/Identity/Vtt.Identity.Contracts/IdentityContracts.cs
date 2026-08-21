namespace Vtt.Identity.Contracts;

public sealed record RegisterUserRequest(
    string Email,
    string Password,
    string DisplayName,
    string Locale,
    string TimeZone,
    string TermsVersion,
    string PrivacyVersion);

public sealed record ChallengeTokenRequest(string ChallengeToken);

public sealed record PasswordResetRequest(string Email);

public sealed record PasswordResetCompleteRequest(
    string ChallengeToken,
    string NewPassword);

public sealed record LoginRequest(
    string Email,
    string Password,
    string? DeviceLabel);

public sealed record GenericMessageResponse(string Message);

public sealed record BrowserSessionResponse(
    string CsrfToken,
    DateTimeOffset ExpiresAt,
    UserProfileResponse User);

public sealed record InternalSessionResponse(
    string SessionToken,
    string CsrfToken,
    DateTimeOffset ExpiresAt,
    UserProfileResponse User);

public sealed record UserProfileResponse(
    Guid UserId,
    string DisplayName,
    string Locale,
    string TimeZone,
    long Version,
    long SecurityRevision);

public sealed record UpdateProfileRequest(
    string DisplayName,
    string Locale,
    string TimeZone);

public sealed record LoginSessionResponse(
    Guid SessionId,
    string DeviceLabel,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastUsedAt,
    DateTimeOffset ExpiresAt,
    bool IsCurrent,
    bool IsRevoked);

public sealed record UserActivatedV1(
    Guid UserId,
    long DisplayNameRevision,
    DateTimeOffset OccurredAt);

public sealed record UserProfileChangedV1(
    Guid UserId,
    long PublicProfileRevision,
    DateTimeOffset OccurredAt);

public sealed record UserDeactivatedV1(
    Guid UserId,
    DateTimeOffset EffectiveAt);

public sealed record UserSecurityStampChangedV1(
    Guid UserId,
    long StampRevision,
    DateTimeOffset OccurredAt);
