namespace Vtt.Identity.Domain;

public enum AccountStatus
{
    Pending,
    Active,
    Locked,
    DeactivationPending,
}

public enum SecurityChallengePurpose
{
    EmailVerification,
    PasswordReset,
}

public enum SessionRotationResult
{
    Rotated,
    BenignRace,
    ReuseDetected,
    Rejected,
}

public abstract record IdentityDomainEvent(Guid UserId, DateTimeOffset OccurredAt);

public sealed record UserRegistered(
    Guid UserId,
    DateTimeOffset OccurredAt) : IdentityDomainEvent(UserId, OccurredAt);

public sealed record EmailVerified(
    Guid UserId,
    DateTimeOffset OccurredAt) : IdentityDomainEvent(UserId, OccurredAt);

public sealed record UserProfileChanged(
    Guid UserId,
    long ProfileRevision,
    DateTimeOffset OccurredAt) : IdentityDomainEvent(UserId, OccurredAt);

public sealed record UserSecurityStampChanged(
    Guid UserId,
    long StampRevision,
    DateTimeOffset OccurredAt) : IdentityDomainEvent(UserId, OccurredAt);

public sealed record UserDeactivationRequested(
    Guid UserId,
    DateTimeOffset OccurredAt) : IdentityDomainEvent(UserId, OccurredAt);

public sealed class UserAccount
{
    private readonly List<IdentityDomainEvent> _events = [];

    private UserAccount()
    {
    }

    private UserAccount(
        Guid id,
        string displayName,
        string locale,
        string timeZone,
        string termsVersion,
        string privacyVersion,
        DateTimeOffset now)
    {
        Id = id;
        Status = AccountStatus.Pending;
        var normalizedDisplayName = NormalizeDisplayName(displayName);
        var normalizedLocale = NormalizeLocale(locale);
        var normalizedTimeZone = NormalizeTimeZone(timeZone);

        DisplayName = normalizedDisplayName;
        Locale = normalizedLocale;
        TimeZone = normalizedTimeZone;
        TermsVersion = RequireVersion(termsVersion, nameof(termsVersion));
        PrivacyVersion = RequireVersion(privacyVersion, nameof(privacyVersion));
        CreatedAt = now;
        UpdatedAt = now;
        Version = 1;
        SecurityRevision = 1;
        _events.Add(new UserRegistered(id, now));
    }

    public Guid Id { get; private set; }

    public AccountStatus Status { get; private set; }

    public string DisplayName { get; private set; } = string.Empty;

    public string Locale { get; private set; } = string.Empty;

    public string TimeZone { get; private set; } = string.Empty;

    public string TermsVersion { get; private set; } = string.Empty;

    public string PrivacyVersion { get; private set; } = string.Empty;

    public long Version { get; private set; }

    public long SecurityRevision { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? EmailVerifiedAt { get; private set; }

    public DateTimeOffset? DeactivationRequestedAt { get; private set; }

    public IReadOnlyCollection<IdentityDomainEvent> Events => _events;

    public static UserAccount Register(
        Guid id,
        string displayName,
        string locale,
        string timeZone,
        string termsVersion,
        string privacyVersion,
        DateTimeOffset now)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("User id must not be empty.", nameof(id));
        }

        return new UserAccount(
            id,
            displayName,
            locale,
            timeZone,
            termsVersion,
            privacyVersion,
            now);
    }

    public bool VerifyEmail(DateTimeOffset now)
    {
        if (EmailVerifiedAt is not null)
        {
            return false;
        }

        EmailVerifiedAt = now;
        Status = AccountStatus.Active;
        Touch(now);
        _events.Add(new EmailVerified(Id, now));
        return true;
    }

    public void UpdateProfile(
        string displayName,
        string locale,
        string timeZone,
        long expectedVersion,
        DateTimeOffset now)
    {
        if (Version != expectedVersion)
        {
            throw new AccountVersionConflictException(Version);
        }

        var normalizedDisplayName = NormalizeDisplayName(displayName);
        var normalizedLocale = NormalizeLocale(locale);
        var normalizedTimeZone = NormalizeTimeZone(timeZone);

        DisplayName = normalizedDisplayName;
        Locale = normalizedLocale;
        TimeZone = normalizedTimeZone;
        Touch(now);
        _events.Add(new UserProfileChanged(Id, Version, now));
    }

    public void RotateSecurityStamp(DateTimeOffset now)
    {
        SecurityRevision++;
        Touch(now);
        _events.Add(new UserSecurityStampChanged(Id, SecurityRevision, now));
    }

    public void RequestDeactivation(DateTimeOffset now)
    {
        if (Status == AccountStatus.DeactivationPending)
        {
            return;
        }

        Status = AccountStatus.DeactivationPending;
        DeactivationRequestedAt = now;
        RotateSecurityStamp(now);
        _events.Add(new UserDeactivationRequested(Id, now));
    }

    public IReadOnlyCollection<IdentityDomainEvent> DequeueEvents()
    {
        var events = _events.ToArray();
        _events.Clear();
        return events;
    }

    private void Touch(DateTimeOffset now)
    {
        Version++;
        UpdatedAt = now;
    }

    private static string NormalizeDisplayName(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var normalized = value.Trim();
        if (normalized.Length is < 2 or > 80)
        {
            throw new ArgumentException("Display name must contain 2 to 80 characters.", nameof(value));
        }

        return normalized;
    }

    private static string NormalizeLocale(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var normalized = value.Trim();
        if (normalized.Length is < 2 or > 16 ||
            normalized.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
        {
            throw new ArgumentException("Locale has an invalid format.", nameof(value));
        }

        return normalized;
    }

    private static string NormalizeTimeZone(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var normalized = value.Trim();
        if (normalized.Length is < 1 or > 100 || normalized.Any(char.IsControl))
        {
            throw new ArgumentException("Time zone has an invalid format.", nameof(value));
        }

        return normalized;
    }

    private static string RequireVersion(string value, string parameterName)
    {
        var normalized = value.Trim();
        if (normalized.Length is < 1 or > 32)
        {
            throw new ArgumentException("Consent version is required.", parameterName);
        }

        return normalized;
    }
}

public sealed class LoginSession
{
    private LoginSession()
    {
    }

    private LoginSession(
        Guid id,
        Guid userId,
        Guid familyId,
        string tokenHash,
        string csrfHash,
        string deviceLabel,
        long securityRevision,
        DateTimeOffset now,
        DateTimeOffset expiresAt)
    {
        Id = id;
        UserId = userId;
        FamilyId = familyId;
        CurrentTokenHash = tokenHash;
        CsrfHash = csrfHash;
        DeviceLabel = NormalizeDeviceLabel(deviceLabel);
        SecurityRevision = securityRevision;
        CreatedAt = now;
        LastUsedAt = now;
        ExpiresAt = expiresAt;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public Guid FamilyId { get; private set; }

    public string CurrentTokenHash { get; private set; } = string.Empty;

    public string? PreviousTokenHash { get; private set; }

    public DateTimeOffset? PreviousTokenGraceUntil { get; private set; }

    public string CsrfHash { get; private set; } = string.Empty;

    public string DeviceLabel { get; private set; } = string.Empty;

    public long SecurityRevision { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset LastUsedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public string? RevocationReason { get; private set; }

    public bool IsRevoked => RevokedAt is not null;

    public static LoginSession Issue(
        Guid userId,
        string tokenHash,
        string csrfHash,
        string deviceLabel,
        long securityRevision,
        DateTimeOffset now,
        TimeSpan lifetime) =>
        new(
            Guid.CreateVersion7(),
            userId,
            Guid.CreateVersion7(),
            tokenHash,
            csrfHash,
            deviceLabel,
            securityRevision,
            now,
            now.Add(lifetime));

    public bool CanAuthenticate(bool tokenMatches, long currentSecurityRevision, DateTimeOffset now) =>
        tokenMatches &&
        !IsRevoked &&
        ExpiresAt > now &&
        SecurityRevision == currentSecurityRevision;

    public bool ValidateCsrf(bool csrfMatches) => !IsRevoked && csrfMatches;

    public SessionRotationResult Rotate(
        bool matchesCurrentToken,
        bool matchesPreviousToken,
        string newTokenHash,
        string newCsrfHash,
        long currentSecurityRevision,
        DateTimeOffset now,
        TimeSpan benignRaceWindow)
    {
        if (IsRevoked || ExpiresAt <= now || SecurityRevision != currentSecurityRevision)
        {
            return SessionRotationResult.Rejected;
        }

        if (matchesCurrentToken)
        {
            PreviousTokenHash = CurrentTokenHash;
            PreviousTokenGraceUntil = now.Add(benignRaceWindow);
            CurrentTokenHash = newTokenHash;
            CsrfHash = newCsrfHash;
            LastUsedAt = now;
            return SessionRotationResult.Rotated;
        }

        if (matchesPreviousToken && PreviousTokenGraceUntil >= now)
        {
            return SessionRotationResult.BenignRace;
        }

        if (matchesPreviousToken)
        {
            Revoke("refresh_token_reuse", now);
            return SessionRotationResult.ReuseDetected;
        }

        return SessionRotationResult.Rejected;
    }

    public void Revoke(string reason, DateTimeOffset now)
    {
        if (IsRevoked)
        {
            return;
        }

        RevokedAt = now;
        RevocationReason = reason.Length <= 64 ? reason : reason[..64];
    }

    private static string NormalizeDeviceLabel(string value)
    {
        var normalized = string.Join(' ', value.Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        return normalized.Length switch
        {
            0 => "Unknown device",
            > 120 => normalized[..120],
            _ => normalized,
        };
    }
}

public sealed class SecurityChallenge
{
    private SecurityChallenge()
    {
    }

    private SecurityChallenge(
        Guid id,
        Guid userId,
        SecurityChallengePurpose purpose,
        string tokenHash,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt)
    {
        Id = id;
        UserId = userId;
        Purpose = purpose;
        TokenHash = tokenHash;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public SecurityChallengePurpose Purpose { get; private set; }

    public string TokenHash { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? ConsumedAt { get; private set; }

    public static SecurityChallenge Create(
        Guid userId,
        SecurityChallengePurpose purpose,
        string tokenHash,
        DateTimeOffset now,
        TimeSpan lifetime) =>
        new(Guid.CreateVersion7(), userId, purpose, tokenHash, now, now.Add(lifetime));

    public bool TryConsume(DateTimeOffset now)
    {
        if (ConsumedAt is not null || ExpiresAt <= now)
        {
            return false;
        }

        ConsumedAt = now;
        return true;
    }
}

public sealed class AccountVersionConflictException(long currentVersion)
    : Exception("The account profile was modified by another request.")
{
    public long CurrentVersion { get; } = currentVersion;
}
