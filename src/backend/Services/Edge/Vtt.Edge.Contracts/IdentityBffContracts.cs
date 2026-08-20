namespace Vtt.Edge.Contracts;

public sealed record RegisterRequest(
    string Email,
    string Password,
    string DisplayName,
    string Locale = "ru-RU",
    string TimeZone = "Europe/Moscow",
    string TermsVersion = "2026-01",
    string PrivacyVersion = "2026-01");

public sealed record LoginRequest(string Email, string Password, string? DeviceLabel = null);
public sealed record TokenRequest(string ChallengeToken);
public sealed record ResetRequest(string Email);
public sealed record ResetCompleteRequest(string ChallengeToken, string NewPassword);
public sealed record UpdateProfileRequest(string DisplayName, string Locale, string TimeZone, long Version);
public sealed record SessionResponse(DateTimeOffset ExpiresAt, UserResponse User);
public sealed record UserResponse(Guid UserId, string DisplayName, string Locale, string TimeZone, long Version);
