using System.Globalization;
using System.Net.Mail;
using System.Text;
using System.Diagnostics.Metrics;
using Vtt.Identity.Domain;

namespace Vtt.Identity.Application;

internal static class IdentityTelemetry
{
    private static readonly Meter Meter = new("Vtt.Identity", "1.0.0");
    private static readonly Counter<long> AuthenticationAttempts =
        Meter.CreateCounter<long>("vtt.identity.authentication.attempts");
    private static readonly Counter<long> ChallengeFailures =
        Meter.CreateCounter<long>("vtt.identity.challenge.failures");
    private static readonly Counter<long> RefreshReuse =
        Meter.CreateCounter<long>("vtt.identity.refresh.reuse");

    public static void RecordAuthentication(string outcome) =>
        AuthenticationAttempts.Add(1, new KeyValuePair<string, object?>("outcome", outcome));

    public static void RecordChallengeFailure(string purpose) =>
        ChallengeFailures.Add(1, new KeyValuePair<string, object?>("purpose", purpose));

    public static void RecordRefreshReuse() => RefreshReuse.Add(1);
}

public sealed record IdentityPolicies(
    TimeSpan EmailVerificationLifetime,
    TimeSpan PasswordResetLifetime,
    TimeSpan SessionLifetime,
    TimeSpan RefreshRaceWindow,
    int MaxFailedLoginAttempts,
    TimeSpan LockoutDuration)
{
    public static IdentityPolicies Default { get; } = new(
        TimeSpan.FromHours(24),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromDays(30),
        TimeSpan.FromSeconds(5),
        5,
        TimeSpan.FromMinutes(15));
}

public sealed record RegistrationCommand(
    string Email,
    string Password,
    string DisplayName,
    string Locale,
    string TimeZone,
    string TermsVersion,
    string PrivacyVersion);

public sealed record ProfileUpdateCommand(
    string DisplayName,
    string Locale,
    string TimeZone,
    long ExpectedVersion);

public sealed record GenericAcceptance(string Message);

public enum LoginOutcome
{
    Succeeded,
    Rejected,
}

public enum RefreshOutcome
{
    Rotated,
    BenignRace,
    ReuseDetected,
    Rejected,
}

public sealed record LoginResult(
    LoginOutcome Outcome,
    IssuedSession? Session = null);

public sealed record RefreshResult(
    RefreshOutcome Outcome,
    IssuedSession? Session = null);

public sealed record IssuedSession(
    string SessionToken,
    string CsrfToken,
    DateTimeOffset ExpiresAt,
    UserSummary User);

public sealed record UserSummary(
    Guid UserId,
    string DisplayName,
    string Locale,
    string TimeZone,
    long Version,
    long SecurityRevision);

public sealed record LoginSessionSummary(
    Guid SessionId,
    string DeviceLabel,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastUsedAt,
    DateTimeOffset ExpiresAt,
    bool IsCurrent,
    bool IsRevoked);

public sealed record AuthenticatedSession(
    UserAccount Account,
    LoginSession Session);

public sealed record CredentialSnapshot(
    Guid UserId,
    string PasswordHash,
    int FailedLoginCount,
    DateTimeOffset? LockoutEnd);

public interface IIdentityStore
{
    Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken cancellationToken);

    Task<CredentialSnapshot?> FindCredentialByEmailAsync(
        string normalizedEmail,
        CancellationToken cancellationToken);

    Task<UserAccount?> FindAccountAsync(Guid userId, CancellationToken cancellationToken);

    Task AddRegistrationAsync(
        UserAccount account,
        string email,
        string normalizedEmail,
        string passwordHash,
        SecurityChallenge challenge,
        string rawChallengeToken,
        CancellationToken cancellationToken);

    Task<SecurityChallenge?> FindChallengeAsync(
        string tokenHash,
        SecurityChallengePurpose purpose,
        CancellationToken cancellationToken);

    Task AddChallengeAsync(
        SecurityChallenge challenge,
        string rawChallengeToken,
        CancellationToken cancellationToken);

    Task UpdateCredentialFailuresAsync(
        Guid userId,
        int failedCount,
        DateTimeOffset? lockoutEnd,
        CancellationToken cancellationToken);

    Task ReplacePasswordHashAsync(
        Guid userId,
        string passwordHash,
        CancellationToken cancellationToken);

    Task AddSessionAsync(LoginSession session, CancellationToken cancellationToken);

    Task<LoginSession?> FindSessionAsync(Guid sessionId, CancellationToken cancellationToken);

    Task<IReadOnlyList<LoginSession>> ListSessionsAsync(
        Guid userId,
        CancellationToken cancellationToken);

    Task RevokeSessionsAsync(
        Guid userId,
        Guid? exceptSessionId,
        string reason,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    void AppendDomainEvents(IEnumerable<IdentityDomainEvent> events);

    void AppendSecurityAudit(
        Guid? userId,
        Guid? sessionId,
        string eventType,
        DateTimeOffset occurredAt);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IPasswordCredentialService
{
    string Hash(Guid userId, string password);

    bool Verify(Guid userId, string passwordHash, string password);

    void BurnVerificationTime(string password);
}

public interface IIdentitySecretService
{
    string Generate();

    string Hash(string secret);

    bool Verify(string secret, string expectedHash);
}

public interface IIdentityClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class IdentityAccessService(
    IIdentityStore store,
    IPasswordCredentialService passwords,
    IIdentitySecretService secrets,
    IIdentityClock clock,
    IdentityPolicies policies)
{
    public const string GenericRegistrationMessage =
        "Если адрес может быть использован, инструкция уже отправлена.";

    public const string GenericResetMessage =
        "Если аккаунт существует, инструкция уже отправлена.";

    public async Task<GenericAcceptance> RegisterAsync(
        RegistrationCommand command,
        CancellationToken cancellationToken)
    {
        ValidatePassword(command.Password);
        var normalizedEmail = NormalizeEmail(command.Email);
        var userId = Guid.CreateVersion7();
        var passwordHash = passwords.Hash(userId, command.Password);

        if (await store.EmailExistsAsync(normalizedEmail, cancellationToken))
        {
            return new GenericAcceptance(GenericRegistrationMessage);
        }

        var now = clock.UtcNow;
        var account = UserAccount.Register(
            userId,
            command.DisplayName,
            command.Locale,
            command.TimeZone,
            command.TermsVersion,
            command.PrivacyVersion,
            now);
        var rawToken = secrets.Generate();
        var challenge = SecurityChallenge.Create(
            userId,
            SecurityChallengePurpose.EmailVerification,
            secrets.Hash(rawToken),
            now,
            policies.EmailVerificationLifetime);

        await store.AddRegistrationAsync(
            account,
            command.Email.Trim(),
            normalizedEmail,
            passwordHash,
            challenge,
            rawToken,
            cancellationToken);
        store.AppendDomainEvents(account.DequeueEvents());
        store.AppendSecurityAudit(userId, null, "registration_accepted", now);
        await store.SaveChangesAsync(cancellationToken);

        return new GenericAcceptance(GenericRegistrationMessage);
    }

    public async Task<bool> VerifyEmailAsync(
        string rawToken,
        CancellationToken cancellationToken)
    {
        var challenge = await store.FindChallengeAsync(
            secrets.Hash(RequireToken(rawToken)),
            SecurityChallengePurpose.EmailVerification,
            cancellationToken);
        if (challenge is null || !challenge.TryConsume(clock.UtcNow))
        {
            IdentityTelemetry.RecordChallengeFailure("email_verification");
            return false;
        }

        var account = await store.FindAccountAsync(challenge.UserId, cancellationToken);
        if (account is null || !account.VerifyEmail(clock.UtcNow))
        {
            return false;
        }

        store.AppendDomainEvents(account.DequeueEvents());
        store.AppendSecurityAudit(account.Id, null, "email_verified", clock.UtcNow);
        await store.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<GenericAcceptance> RequestPasswordResetAsync(
        string email,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = NormalizeEmail(email);
        var credential = await store.FindCredentialByEmailAsync(normalizedEmail, cancellationToken);
        if (credential is null)
        {
            return new GenericAcceptance(GenericResetMessage);
        }

        var account = await store.FindAccountAsync(credential.UserId, cancellationToken);
        if (account is null || account.Status == AccountStatus.DeactivationPending)
        {
            return new GenericAcceptance(GenericResetMessage);
        }

        var rawToken = secrets.Generate();
        var challenge = SecurityChallenge.Create(
            account.Id,
            SecurityChallengePurpose.PasswordReset,
            secrets.Hash(rawToken),
            clock.UtcNow,
            policies.PasswordResetLifetime);
        await store.AddChallengeAsync(challenge, rawToken, cancellationToken);
        store.AppendSecurityAudit(account.Id, null, "password_reset_requested", clock.UtcNow);
        await store.SaveChangesAsync(cancellationToken);
        return new GenericAcceptance(GenericResetMessage);
    }

    public async Task<bool> CompletePasswordResetAsync(
        string rawToken,
        string newPassword,
        CancellationToken cancellationToken)
    {
        ValidatePassword(newPassword);
        var challenge = await store.FindChallengeAsync(
            secrets.Hash(RequireToken(rawToken)),
            SecurityChallengePurpose.PasswordReset,
            cancellationToken);
        if (challenge is null || !challenge.TryConsume(clock.UtcNow))
        {
            IdentityTelemetry.RecordChallengeFailure("password_reset");
            return false;
        }

        var account = await store.FindAccountAsync(challenge.UserId, cancellationToken);
        if (account is null)
        {
            return false;
        }

        await store.ReplacePasswordHashAsync(
            account.Id,
            passwords.Hash(account.Id, newPassword),
            cancellationToken);
        account.RotateSecurityStamp(clock.UtcNow);
        await store.RevokeSessionsAsync(
            account.Id,
            null,
            "password_reset",
            clock.UtcNow,
            cancellationToken);
        store.AppendDomainEvents(account.DequeueEvents());
        store.AppendSecurityAudit(account.Id, null, "password_reset_completed", clock.UtcNow);
        await store.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<LoginResult> LoginAsync(
        string email,
        string password,
        string deviceLabel,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = NormalizeEmail(email);
        var credential = await store.FindCredentialByEmailAsync(normalizedEmail, cancellationToken);
        if (credential is null)
        {
            passwords.BurnVerificationTime(password);
            IdentityTelemetry.RecordAuthentication("rejected");
            store.AppendSecurityAudit(null, null, "authentication_failed", clock.UtcNow);
            await store.SaveChangesAsync(cancellationToken);
            return new LoginResult(LoginOutcome.Rejected);
        }

        var account = await store.FindAccountAsync(credential.UserId, cancellationToken);
        var locked = credential.LockoutEnd > clock.UtcNow;
        var passwordValid = passwords.Verify(credential.UserId, credential.PasswordHash, password);
        if (account is null || locked || !passwordValid || account.Status != AccountStatus.Active)
        {
            IdentityTelemetry.RecordAuthentication(locked ? "locked" : "rejected");
            if (!locked && !passwordValid)
            {
                var failures = credential.FailedLoginCount + 1;
                DateTimeOffset? lockoutEnd = failures >= policies.MaxFailedLoginAttempts
                    ? clock.UtcNow.Add(policies.LockoutDuration)
                    : null;
                await store.UpdateCredentialFailuresAsync(
                    credential.UserId,
                    failures,
                    lockoutEnd,
                    cancellationToken);
            }

            store.AppendSecurityAudit(credential.UserId, null, "authentication_failed", clock.UtcNow);
            await store.SaveChangesAsync(cancellationToken);
            return new LoginResult(LoginOutcome.Rejected);
        }

        await store.UpdateCredentialFailuresAsync(account.Id, 0, null, cancellationToken);
        IdentityTelemetry.RecordAuthentication("succeeded");
        var issued = await IssueSessionAsync(account, deviceLabel, cancellationToken);
        store.AppendSecurityAudit(account.Id, issued.SessionId, "session_issued", clock.UtcNow);
        await store.SaveChangesAsync(cancellationToken);
        return new LoginResult(LoginOutcome.Succeeded, issued.Result);
    }

    public async Task<RefreshResult> RefreshAsync(
        string sessionToken,
        CancellationToken cancellationToken)
    {
        if (!TryParseSessionToken(sessionToken, out var sessionId, out var rawSecret))
        {
            return new RefreshResult(RefreshOutcome.Rejected);
        }

        var session = await store.FindSessionAsync(sessionId, cancellationToken);
        if (session is null)
        {
            return new RefreshResult(RefreshOutcome.Rejected);
        }

        var account = await store.FindAccountAsync(session.UserId, cancellationToken);
        if (account is null)
        {
            return new RefreshResult(RefreshOutcome.Rejected);
        }

        var newSecret = secrets.Generate();
        var newCsrf = secrets.Generate();
        var rotation = session.Rotate(
            secrets.Verify(rawSecret, session.CurrentTokenHash),
            session.PreviousTokenHash is not null &&
            secrets.Verify(rawSecret, session.PreviousTokenHash),
            secrets.Hash(newSecret),
            secrets.Hash(newCsrf),
            account.SecurityRevision,
            clock.UtcNow,
            policies.RefreshRaceWindow);

        switch (rotation)
        {
            case SessionRotationResult.Rotated:
                store.AppendSecurityAudit(account.Id, session.Id, "session_rotated", clock.UtcNow);
                await store.SaveChangesAsync(cancellationToken);
                return new RefreshResult(
                    RefreshOutcome.Rotated,
                    CreateIssuedSession(account, session, newSecret, newCsrf));
            case SessionRotationResult.BenignRace:
                return new RefreshResult(RefreshOutcome.BenignRace);
            case SessionRotationResult.ReuseDetected:
                IdentityTelemetry.RecordRefreshReuse();
                await store.RevokeSessionsAsync(
                    account.Id,
                    null,
                    "refresh_token_reuse",
                    clock.UtcNow,
                    cancellationToken);
                store.AppendSecurityAudit(account.Id, session.Id, "refresh_reuse_detected", clock.UtcNow);
                await store.SaveChangesAsync(cancellationToken);
                return new RefreshResult(RefreshOutcome.ReuseDetected);
            default:
                return new RefreshResult(RefreshOutcome.Rejected);
        }
    }

    public async Task<AuthenticatedSession?> AuthenticateAsync(
        string sessionToken,
        string? csrfToken,
        bool requireCsrf,
        CancellationToken cancellationToken)
    {
        if (!TryParseSessionToken(sessionToken, out var sessionId, out var rawSecret))
        {
            return null;
        }

        var session = await store.FindSessionAsync(sessionId, cancellationToken);
        if (session is null)
        {
            return null;
        }

        var account = await store.FindAccountAsync(session.UserId, cancellationToken);
        if (account is null ||
            !session.CanAuthenticate(
                secrets.Verify(rawSecret, session.CurrentTokenHash),
                account.SecurityRevision,
                clock.UtcNow) ||
            requireCsrf &&
            (string.IsNullOrWhiteSpace(csrfToken) ||
             !session.ValidateCsrf(secrets.Verify(csrfToken, session.CsrfHash))))
        {
            return null;
        }

        return new AuthenticatedSession(account, session);
    }

    public async Task<UserSummary?> GetMeAsync(
        string sessionToken,
        CancellationToken cancellationToken)
    {
        var authenticated = await AuthenticateAsync(
            sessionToken,
            null,
            false,
            cancellationToken);
        return authenticated is null ? null : ToSummary(authenticated.Account);
    }

    public async Task<UserSummary?> UpdateProfileAsync(
        string sessionToken,
        string csrfToken,
        ProfileUpdateCommand command,
        CancellationToken cancellationToken)
    {
        var authenticated = await AuthenticateAsync(
            sessionToken,
            csrfToken,
            true,
            cancellationToken);
        if (authenticated is null)
        {
            return null;
        }

        authenticated.Account.UpdateProfile(
            command.DisplayName,
            command.Locale,
            command.TimeZone,
            command.ExpectedVersion,
            clock.UtcNow);
        store.AppendDomainEvents(authenticated.Account.DequeueEvents());
        store.AppendSecurityAudit(
            authenticated.Account.Id,
            authenticated.Session.Id,
            "profile_updated",
            clock.UtcNow);
        await store.SaveChangesAsync(cancellationToken);
        return ToSummary(authenticated.Account);
    }

    public async Task<IReadOnlyList<LoginSessionSummary>?> ListSessionsAsync(
        string sessionToken,
        CancellationToken cancellationToken)
    {
        var authenticated = await AuthenticateAsync(
            sessionToken,
            null,
            false,
            cancellationToken);
        if (authenticated is null)
        {
            return null;
        }

        var sessions = await store.ListSessionsAsync(authenticated.Account.Id, cancellationToken);
        return sessions
            .OrderByDescending(session => session.LastUsedAt)
            .Select(session => new LoginSessionSummary(
                session.Id,
                session.DeviceLabel,
                session.CreatedAt,
                session.LastUsedAt,
                session.ExpiresAt,
                session.Id == authenticated.Session.Id,
                session.IsRevoked))
            .ToArray();
    }

    public async Task<bool> RevokeSessionAsync(
        string sessionToken,
        string csrfToken,
        Guid targetSessionId,
        CancellationToken cancellationToken)
    {
        var authenticated = await AuthenticateAsync(
            sessionToken,
            csrfToken,
            true,
            cancellationToken);
        if (authenticated is null)
        {
            return false;
        }

        var target = await store.FindSessionAsync(targetSessionId, cancellationToken);
        if (target is null || target.UserId != authenticated.Account.Id)
        {
            return false;
        }

        target.Revoke("user_revoked", clock.UtcNow);
        store.AppendSecurityAudit(target.UserId, target.Id, "session_revoked", clock.UtcNow);
        await store.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> RevokeOtherSessionsAsync(
        string sessionToken,
        string csrfToken,
        CancellationToken cancellationToken)
    {
        var authenticated = await AuthenticateAsync(
            sessionToken,
            csrfToken,
            true,
            cancellationToken);
        if (authenticated is null)
        {
            return false;
        }

        await store.RevokeSessionsAsync(
            authenticated.Account.Id,
            authenticated.Session.Id,
            "revoke_others",
            clock.UtcNow,
            cancellationToken);
        store.AppendSecurityAudit(
            authenticated.Account.Id,
            authenticated.Session.Id,
            "other_sessions_revoked",
            clock.UtcNow);
        await store.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> LogoutAsync(
        string sessionToken,
        string csrfToken,
        CancellationToken cancellationToken)
    {
        var authenticated = await AuthenticateAsync(
            sessionToken,
            csrfToken,
            true,
            cancellationToken);
        if (authenticated is null)
        {
            return false;
        }

        authenticated.Session.Revoke("logout", clock.UtcNow);
        store.AppendSecurityAudit(
            authenticated.Account.Id,
            authenticated.Session.Id,
            "logout",
            clock.UtcNow);
        await store.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<(Guid SessionId, IssuedSession Result)> IssueSessionAsync(
        UserAccount account,
        string deviceLabel,
        CancellationToken cancellationToken)
    {
        var rawSecret = secrets.Generate();
        var rawCsrf = secrets.Generate();
        var session = LoginSession.Issue(
            account.Id,
            secrets.Hash(rawSecret),
            secrets.Hash(rawCsrf),
            deviceLabel,
            account.SecurityRevision,
            clock.UtcNow,
            policies.SessionLifetime);
        await store.AddSessionAsync(session, cancellationToken);
        return (session.Id, CreateIssuedSession(account, session, rawSecret, rawCsrf));
    }

    private static IssuedSession CreateIssuedSession(
        UserAccount account,
        LoginSession session,
        string rawSecret,
        string rawCsrf) =>
        new(
            $"{session.Id:N}.{rawSecret}",
            rawCsrf,
            session.ExpiresAt,
            ToSummary(account));

    private static UserSummary ToSummary(UserAccount account) =>
        new(
            account.Id,
            account.DisplayName,
            account.Locale,
            account.TimeZone,
            account.Version,
            account.SecurityRevision);

    private static bool TryParseSessionToken(
        string token,
        out Guid sessionId,
        out string secret)
    {
        sessionId = Guid.Empty;
        secret = string.Empty;
        if (string.IsNullOrWhiteSpace(token) || token.Length > 256)
        {
            return false;
        }

        var separator = token.IndexOf('.');
        if (separator != 32 ||
            !Guid.TryParseExact(token[..separator], "N", out sessionId) ||
            token.Length <= separator + 20)
        {
            sessionId = Guid.Empty;
            return false;
        }

        secret = token[(separator + 1)..];
        return secret.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
    }

    private static string NormalizeEmail(string value)
    {
        var candidate = value.Trim().Normalize(NormalizationForm.FormKC);
        if (candidate.Length is < 3 or > 254)
        {
            throw new ArgumentException("Email has an invalid format.", nameof(value));
        }

        try
        {
            var address = new MailAddress(candidate);
            if (!string.Equals(address.Address, candidate, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Email has an invalid format.", nameof(value));
            }
        }
        catch (FormatException exception)
        {
            throw new ArgumentException("Email has an invalid format.", nameof(value), exception);
        }

        return candidate.ToUpper(CultureInfo.InvariantCulture);
    }

    private static void ValidatePassword(string password)
    {
        if (password.Length is < 12 or > 128 ||
            !password.Any(char.IsUpper) ||
            !password.Any(char.IsLower) ||
            !password.Any(char.IsDigit) ||
            !password.Any(character => !char.IsLetterOrDigit(character)))
        {
            throw new PasswordPolicyException();
        }
    }

    private static string RequireToken(string token)
    {
        var value = token.Trim();
        if (value.Length is < 20 or > 256 ||
            value.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
        {
            throw new ArgumentException("Challenge token has an invalid format.", nameof(token));
        }

        return value;
    }
}

public sealed class PasswordPolicyException()
    : Exception("Password does not satisfy the active password policy.");
