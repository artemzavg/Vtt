using Vtt.Identity.Domain;

namespace Vtt.Identity.UnitTests;

public sealed class IdentityDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void EmailVerificationActivatesPendingAccountOnlyOnce()
    {
        var account = CreateAccount();

        Assert.True(account.VerifyEmail(Now.AddMinutes(1)));
        Assert.False(account.VerifyEmail(Now.AddMinutes(2)));
        Assert.Equal(AccountStatus.Active, account.Status);
        Assert.Equal(2, account.Version);
        Assert.Single(account.Events.OfType<EmailVerified>());
    }

    [Fact]
    public void ProfileUpdateRejectsStaleExpectedVersion()
    {
        var account = CreateAccount();
        account.UpdateProfile("Алия", "ru-RU", "Europe/Moscow", 1, Now.AddMinutes(1));

        var exception = Assert.Throws<AccountVersionConflictException>(() =>
            account.UpdateProfile("Пол", "en-US", "UTC", 1, Now.AddMinutes(2)));

        Assert.Equal(2, exception.CurrentVersion);
        Assert.Equal("Алия", account.DisplayName);
    }

    [Fact]
    public void ProfileUpdateRejectsMissingRequiredFieldsWithoutChangingAccount()
    {
        var account = CreateAccount();

        Assert.Throws<ArgumentNullException>(() =>
            account.UpdateProfile("Пол", null!, "UTC", 1, Now.AddMinutes(1)));

        Assert.Equal("Пол Атрейдес", account.DisplayName);
        Assert.Equal("ru-RU", account.Locale);
        Assert.Equal(1, account.Version);
    }

    [Fact]
    public void ChallengeIsSingleUseAndExpires()
    {
        var challenge = SecurityChallenge.Create(
            Guid.NewGuid(), SecurityChallengePurpose.PasswordReset, "hash", Now, TimeSpan.FromMinutes(30));
        var expired = SecurityChallenge.Create(
            Guid.NewGuid(), SecurityChallengePurpose.PasswordReset, "hash", Now, TimeSpan.FromMinutes(30));

        Assert.True(challenge.TryConsume(Now.AddMinutes(1)));
        Assert.False(challenge.TryConsume(Now.AddMinutes(2)));
        Assert.False(expired.TryConsume(Now.AddMinutes(30)));
    }

    [Fact]
    public void RefreshRotationAllowsShortRaceAndDetectsLaterReuse()
    {
        var userId = Guid.NewGuid();
        var session = LoginSession.Issue(
            userId, "old", "csrf", "Firefox", 1, Now, TimeSpan.FromDays(30));

        Assert.Equal(SessionRotationResult.Rotated,
            session.Rotate(true, false, "new", "new-csrf", 1, Now.AddSeconds(1), TimeSpan.FromSeconds(5)));
        Assert.Equal(SessionRotationResult.BenignRace,
            session.Rotate(false, true, "ignored", "ignored", 1, Now.AddSeconds(5), TimeSpan.FromSeconds(5)));
        Assert.Equal(SessionRotationResult.ReuseDetected,
            session.Rotate(false, true, "ignored", "ignored", 1, Now.AddSeconds(7), TimeSpan.FromSeconds(5)));
        Assert.True(session.IsRevoked);
        Assert.Equal("refresh_token_reuse", session.RevocationReason);
    }

    [Fact]
    public void SecurityStampInvalidatesExistingSession()
    {
        var session = LoginSession.Issue(
            Guid.NewGuid(), "token", "csrf", "Device", 3, Now, TimeSpan.FromDays(1));

        Assert.True(session.CanAuthenticate(true, 3, Now.AddMinutes(1)));
        Assert.False(session.CanAuthenticate(true, 4, Now.AddMinutes(1)));
    }

    private static UserAccount CreateAccount() =>
        UserAccount.Register(Guid.NewGuid(), "Пол Атрейдес", "ru-RU", "Europe/Moscow", "2026-01", "2026-01", Now);
}
