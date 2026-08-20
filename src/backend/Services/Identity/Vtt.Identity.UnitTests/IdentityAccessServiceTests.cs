using System.Security.Cryptography;
using System.Text;
using Vtt.Identity.Application;
using Vtt.Identity.Domain;

namespace Vtt.Identity.UnitTests;

public sealed class IdentityAccessServiceTests
{
    [Fact]
    public async Task RegistrationIsGenericForNewAndExistingEmail()
    {
        var fixture = new Fixture();
        var command = Fixture.Registration();

        var first = await fixture.Service.RegisterAsync(command, CancellationToken.None);
        var second = await fixture.Service.RegisterAsync(command, CancellationToken.None);

        Assert.Equal(first, second);
        Assert.Single(fixture.Store.Accounts);
        Assert.Single(fixture.Store.Credentials);
    }

    [Fact]
    public async Task VerifiedUserCanLoginAndRefreshTokenIsRotated()
    {
        var fixture = new Fixture();
        await fixture.Service.RegisterAsync(Fixture.Registration(), CancellationToken.None);
        Assert.True(await fixture.Service.VerifyEmailAsync(fixture.Store.LastRawChallenge, CancellationToken.None));

        var login = await fixture.Service.LoginAsync(
            "player@example.test", "Correct-horse-42!", "Test browser", CancellationToken.None);
        var refresh = await fixture.Service.RefreshAsync(login.Session!.SessionToken, CancellationToken.None);

        Assert.Equal(LoginOutcome.Succeeded, login.Outcome);
        Assert.Equal(RefreshOutcome.Rotated, refresh.Outcome);
        Assert.NotEqual(login.Session.SessionToken, refresh.Session!.SessionToken);
    }

    [Fact]
    public async Task PasswordResetRevokesAllSessionsAndCannotBeReplayed()
    {
        var fixture = new Fixture();
        await fixture.Service.RegisterAsync(Fixture.Registration(), CancellationToken.None);
        await fixture.Service.VerifyEmailAsync(fixture.Store.LastRawChallenge, CancellationToken.None);
        var login = await fixture.Service.LoginAsync(
            "player@example.test", "Correct-horse-42!", "Test browser", CancellationToken.None);
        await fixture.Service.RequestPasswordResetAsync("player@example.test", CancellationToken.None);
        var token = fixture.Store.LastRawChallenge;

        Assert.True(await fixture.Service.CompletePasswordResetAsync(token, "New-password-84!", CancellationToken.None));
        Assert.False(await fixture.Service.CompletePasswordResetAsync(token, "New-password-84!", CancellationToken.None));
        Assert.Null(await fixture.Service.GetMeAsync(login.Session!.SessionToken, CancellationToken.None));
    }

    private sealed class Fixture
    {
        public Fixture()
        {
            Store = new MemoryStore();
            var secrets = new TestSecrets();
            Service = new IdentityAccessService(
                Store, new TestPasswords(), secrets, new TestClock(), IdentityPolicies.Default);
        }

        public MemoryStore Store { get; }
        public IdentityAccessService Service { get; }

        public static RegistrationCommand Registration() =>
            new("player@example.test", "Correct-horse-42!", "Player", "ru-RU", "Europe/Moscow", "2026-01", "2026-01");
    }

    private sealed class TestClock : IIdentityClock
    {
        public DateTimeOffset UtcNow => new(2026, 8, 15, 10, 0, 0, TimeSpan.Zero);
    }

    private sealed class TestPasswords : IPasswordCredentialService
    {
        public string Hash(Guid userId, string password) => password;
        public bool Verify(Guid userId, string passwordHash, string password) => passwordHash == password;
        public void BurnVerificationTime(string password) { }
    }

    private sealed class TestSecrets : IIdentitySecretService
    {
        private int _value;
        public string Generate() => $"test-secret-{++_value:00000000000000000000}";
        public string Hash(string secret) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
        public bool Verify(string secret, string expectedHash) => Hash(secret) == expectedHash;
    }

    private sealed class MemoryStore : IIdentityStore
    {
        public Dictionary<Guid, UserAccount> Accounts { get; } = [];
        public Dictionary<Guid, CredentialSnapshot> Credentials { get; } = [];
        public Dictionary<Guid, LoginSession> Sessions { get; } = [];
        public List<SecurityChallenge> Challenges { get; } = [];
        public string LastRawChallenge { get; private set; } = string.Empty;

        public Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken cancellationToken) =>
            Task.FromResult(Credentials.Values.Any(item => item.UserId == FindUserId(normalizedEmail)));

        public Task<CredentialSnapshot?> FindCredentialByEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
            Task.FromResult(Credentials.GetValueOrDefault(FindUserId(normalizedEmail)));

        public Task<UserAccount?> FindAccountAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(Accounts.GetValueOrDefault(userId));

        public Task AddRegistrationAsync(UserAccount account, string email, string normalizedEmail, string passwordHash, SecurityChallenge challenge, string rawChallengeToken, CancellationToken cancellationToken)
        {
            Accounts.Add(account.Id, account);
            Credentials.Add(account.Id, new CredentialSnapshot(account.Id, passwordHash, 0, null));
            _emails[normalizedEmail] = account.Id;
            return AddChallengeAsync(challenge, rawChallengeToken, cancellationToken);
        }

        public Task<SecurityChallenge?> FindChallengeAsync(string tokenHash, SecurityChallengePurpose purpose, CancellationToken cancellationToken) =>
            Task.FromResult(Challenges.SingleOrDefault(item => item.TokenHash == tokenHash && item.Purpose == purpose));

        public Task AddChallengeAsync(SecurityChallenge challenge, string rawChallengeToken, CancellationToken cancellationToken)
        {
            Challenges.Add(challenge);
            LastRawChallenge = rawChallengeToken;
            return Task.CompletedTask;
        }

        public Task UpdateCredentialFailuresAsync(Guid userId, int failedCount, DateTimeOffset? lockoutEnd, CancellationToken cancellationToken)
        {
            var current = Credentials[userId];
            Credentials[userId] = current with { FailedLoginCount = failedCount, LockoutEnd = lockoutEnd };
            return Task.CompletedTask;
        }

        public Task ReplacePasswordHashAsync(Guid userId, string passwordHash, CancellationToken cancellationToken)
        {
            Credentials[userId] = Credentials[userId] with { PasswordHash = passwordHash };
            return Task.CompletedTask;
        }

        public Task AddSessionAsync(LoginSession session, CancellationToken cancellationToken)
        {
            Sessions.Add(session.Id, session);
            return Task.CompletedTask;
        }

        public Task<LoginSession?> FindSessionAsync(Guid sessionId, CancellationToken cancellationToken) =>
            Task.FromResult(Sessions.GetValueOrDefault(sessionId));

        public Task<IReadOnlyList<LoginSession>> ListSessionsAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<LoginSession>>(Sessions.Values.Where(item => item.UserId == userId).ToArray());

        public Task RevokeSessionsAsync(Guid userId, Guid? exceptSessionId, string reason, DateTimeOffset now, CancellationToken cancellationToken)
        {
            foreach (var session in Sessions.Values.Where(item => item.UserId == userId && item.Id != exceptSessionId))
            {
                session.Revoke(reason, now);
            }
            return Task.CompletedTask;
        }

        public void AppendDomainEvents(IEnumerable<IdentityDomainEvent> events) { }
        public void AppendSecurityAudit(Guid? userId, Guid? sessionId, string eventType, DateTimeOffset occurredAt) { }
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        private readonly Dictionary<string, Guid> _emails = new(StringComparer.Ordinal);
        private Guid FindUserId(string normalizedEmail) => _emails.GetValueOrDefault(normalizedEmail);
    }
}
