using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MailKit.Net.Smtp;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using Vtt.Identity.Application;
using Vtt.Identity.Domain;

namespace Vtt.Identity.Infrastructure;

public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options)
    : DbContext(options)
{
    public DbSet<UserAccount> Accounts => Set<UserAccount>();

    public DbSet<LoginSession> LoginSessions => Set<LoginSession>();

    public DbSet<SecurityChallenge> SecurityChallenges => Set<SecurityChallenge>();

    public DbSet<CredentialRecord> Credentials => Set<CredentialRecord>();

    public DbSet<EmailOutboxRecord> EmailOutbox => Set<EmailOutboxRecord>();

    public DbSet<SecurityAuditRecord> SecurityAudit => Set<SecurityAuditRecord>();

    public DbSet<IdentityIntegrationEventRecord> IntegrationOutbox =>
        Set<IdentityIntegrationEventRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("identity");
        modelBuilder.UseOpenIddict<Guid>();

        modelBuilder.Entity<UserAccount>(entity =>
        {
            entity.ToTable("user_accounts");
            entity.HasKey(account => account.Id);
            entity.Property(account => account.Status).HasConversion<string>().HasMaxLength(32);
            entity.Property(account => account.DisplayName).HasMaxLength(80);
            entity.Property(account => account.Locale).HasMaxLength(16);
            entity.Property(account => account.TimeZone).HasMaxLength(100);
            entity.Property(account => account.TermsVersion).HasMaxLength(32);
            entity.Property(account => account.PrivacyVersion).HasMaxLength(32);
            entity.Property(account => account.Version).IsConcurrencyToken();
            entity.Ignore(account => account.Events);
        });

        modelBuilder.Entity<CredentialRecord>(entity =>
        {
            entity.ToTable("credentials");
            entity.HasKey(credential => credential.UserId);
            entity.Property(credential => credential.Email).HasMaxLength(254);
            entity.Property(credential => credential.NormalizedEmail).HasMaxLength(254);
            entity.Property(credential => credential.PasswordHash).HasMaxLength(1024);
            entity.HasIndex(credential => credential.NormalizedEmail).IsUnique();
            entity.HasOne<UserAccount>()
                .WithOne()
                .HasForeignKey<CredentialRecord>(credential => credential.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<LoginSession>(entity =>
        {
            entity.ToTable("login_sessions");
            entity.HasKey(session => session.Id);
            entity.Property(session => session.CurrentTokenHash).HasMaxLength(128).IsConcurrencyToken();
            entity.Property(session => session.PreviousTokenHash).HasMaxLength(128);
            entity.Property(session => session.CsrfHash).HasMaxLength(128);
            entity.Property(session => session.DeviceLabel).HasMaxLength(120);
            entity.Property(session => session.RevocationReason).HasMaxLength(64);
            entity.HasIndex(session => new { session.UserId, session.LastUsedAt });
            entity.HasIndex(session => session.FamilyId);
        });

        modelBuilder.Entity<SecurityChallenge>(entity =>
        {
            entity.ToTable("security_challenges");
            entity.HasKey(challenge => challenge.Id);
            entity.Property(challenge => challenge.Purpose).HasConversion<string>().HasMaxLength(32);
            entity.Property(challenge => challenge.TokenHash).HasMaxLength(128);
            entity.Property(challenge => challenge.ConsumedAt).IsConcurrencyToken();
            entity.HasIndex(challenge => challenge.TokenHash).IsUnique();
            entity.HasIndex(challenge => new { challenge.UserId, challenge.Purpose });
        });

        modelBuilder.Entity<EmailOutboxRecord>(entity =>
        {
            entity.ToTable("email_outbox");
            entity.HasKey(message => message.Id);
            entity.Property(message => message.ProtectedPayload).HasMaxLength(8192);
            entity.Property(message => message.LastErrorCode).HasMaxLength(64);
            entity.HasIndex(message => new { message.SentAt, message.NextAttemptAt });
        });

        modelBuilder.Entity<SecurityAuditRecord>(entity =>
        {
            entity.ToTable("security_audit");
            entity.HasKey(record => record.Id);
            entity.Property(record => record.EventType).HasMaxLength(80);
            entity.HasIndex(record => new { record.UserId, record.OccurredAt });
        });

        modelBuilder.Entity<IdentityIntegrationEventRecord>(entity =>
        {
            entity.ToTable("integration_outbox");
            entity.HasKey(record => record.Id);
            entity.Property(record => record.Subject).HasMaxLength(160);
            entity.Property(record => record.Payload).HasColumnType("jsonb");
            entity.HasIndex(record => new { record.PublishedAt, record.OccurredAt });
        });
    }
}

public sealed class CredentialRecord
{
    public Guid UserId { get; set; }

    public string Email { get; set; } = string.Empty;

    public string NormalizedEmail { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public int FailedLoginCount { get; set; }

    public DateTimeOffset? LockoutEnd { get; set; }
}

public sealed class EmailOutboxRecord
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string ProtectedPayload { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset NextAttemptAt { get; set; }

    public int AttemptCount { get; set; }

    public DateTimeOffset? SentAt { get; set; }

    public string? LastErrorCode { get; set; }
}

public sealed class SecurityAuditRecord
{
    public Guid Id { get; set; }

    public Guid? UserId { get; set; }

    public Guid? SessionId { get; set; }

    public string EventType { get; set; } = string.Empty;

    public DateTimeOffset OccurredAt { get; set; }
}

public sealed class IdentityIntegrationEventRecord
{
    public Guid Id { get; set; }

    public string Subject { get; set; } = string.Empty;

    public string Payload { get; set; } = string.Empty;

    public DateTimeOffset OccurredAt { get; set; }

    public DateTimeOffset? PublishedAt { get; set; }
}

public sealed class EfIdentityStore(
    IdentityDbContext dbContext,
    IDataProtectionProvider dataProtectionProvider) : IIdentityStore
{
    private readonly IDataProtector _emailProtector = dataProtectionProvider.CreateProtector(
        "Vtt.Identity.EmailOutbox.v1");

    public Task<bool> EmailExistsAsync(
        string normalizedEmail,
        CancellationToken cancellationToken) =>
        dbContext.Credentials.AnyAsync(
            credential => credential.NormalizedEmail == normalizedEmail,
            cancellationToken);

    public async Task<CredentialSnapshot?> FindCredentialByEmailAsync(
        string normalizedEmail,
        CancellationToken cancellationToken)
    {
        var credential = await dbContext.Credentials.SingleOrDefaultAsync(
            candidate => candidate.NormalizedEmail == normalizedEmail,
            cancellationToken);
        return credential is null
            ? null
            : new CredentialSnapshot(
                credential.UserId,
                credential.PasswordHash,
                credential.FailedLoginCount,
                credential.LockoutEnd);
    }

    public Task<UserAccount?> FindAccountAsync(Guid userId, CancellationToken cancellationToken) =>
        dbContext.Accounts.SingleOrDefaultAsync(account => account.Id == userId, cancellationToken);

    public Task AddRegistrationAsync(
        UserAccount account,
        string email,
        string normalizedEmail,
        string passwordHash,
        SecurityChallenge challenge,
        string rawChallengeToken,
        CancellationToken cancellationToken)
    {
        dbContext.Accounts.Add(account);
        dbContext.Credentials.Add(new CredentialRecord
        {
            UserId = account.Id,
            Email = email,
            NormalizedEmail = normalizedEmail,
            PasswordHash = passwordHash,
        });
        dbContext.SecurityChallenges.Add(challenge);
        EnqueueEmail(
            account.Id,
            email,
            "verify-email",
            rawChallengeToken,
            challenge.ExpiresAt);
        return Task.CompletedTask;
    }

    public Task<SecurityChallenge?> FindChallengeAsync(
        string tokenHash,
        SecurityChallengePurpose purpose,
        CancellationToken cancellationToken) =>
        dbContext.SecurityChallenges.SingleOrDefaultAsync(
            challenge => challenge.TokenHash == tokenHash && challenge.Purpose == purpose,
            cancellationToken);

    public async Task AddChallengeAsync(
        SecurityChallenge challenge,
        string rawChallengeToken,
        CancellationToken cancellationToken)
    {
        var email = await dbContext.Credentials
            .Where(credential => credential.UserId == challenge.UserId)
            .Select(credential => credential.Email)
            .SingleAsync(cancellationToken);
        dbContext.SecurityChallenges.Add(challenge);
        EnqueueEmail(
            challenge.UserId,
            email,
            challenge.Purpose == SecurityChallengePurpose.PasswordReset
                ? "reset-password"
                : "verify-email",
            rawChallengeToken,
            challenge.ExpiresAt);
    }

    public async Task UpdateCredentialFailuresAsync(
        Guid userId,
        int failedCount,
        DateTimeOffset? lockoutEnd,
        CancellationToken cancellationToken)
    {
        var credential = await dbContext.Credentials.SingleAsync(
            candidate => candidate.UserId == userId,
            cancellationToken);
        credential.FailedLoginCount = failedCount;
        credential.LockoutEnd = lockoutEnd;
    }

    public async Task ReplacePasswordHashAsync(
        Guid userId,
        string passwordHash,
        CancellationToken cancellationToken)
    {
        var credential = await dbContext.Credentials.SingleAsync(
            candidate => candidate.UserId == userId,
            cancellationToken);
        credential.PasswordHash = passwordHash;
        credential.FailedLoginCount = 0;
        credential.LockoutEnd = null;
    }

    public Task AddSessionAsync(LoginSession session, CancellationToken cancellationToken)
    {
        dbContext.LoginSessions.Add(session);
        return Task.CompletedTask;
    }

    public Task<LoginSession?> FindSessionAsync(Guid sessionId, CancellationToken cancellationToken) =>
        dbContext.LoginSessions.SingleOrDefaultAsync(
            session => session.Id == sessionId,
            cancellationToken);

    public async Task<IReadOnlyList<LoginSession>> ListSessionsAsync(
        Guid userId,
        CancellationToken cancellationToken) =>
        await dbContext.LoginSessions
            .Where(session => session.UserId == userId)
            .OrderByDescending(session => session.LastUsedAt)
            .Take(100)
            .ToArrayAsync(cancellationToken);

    public async Task RevokeSessionsAsync(
        Guid userId,
        Guid? exceptSessionId,
        string reason,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var sessions = await dbContext.LoginSessions
            .Where(session =>
                session.UserId == userId &&
                session.RevokedAt == null &&
                (!exceptSessionId.HasValue || session.Id != exceptSessionId.Value))
            .ToArrayAsync(cancellationToken);
        foreach (var session in sessions)
        {
            session.Revoke(reason, now);
        }
    }

    public void AppendDomainEvents(IEnumerable<IdentityDomainEvent> events)
    {
        foreach (var domainEvent in events)
        {
            var (subject, payload) = domainEvent switch
            {
                EmailVerified verified => (
                    "vtt.identity.user-activated.v1",
                    JsonSerializer.Serialize(new
                    {
                        userId = verified.UserId,
                        displayNameRevision = 1,
                        occurredAt = verified.OccurredAt,
                    })),
                UserProfileChanged changed => (
                    "vtt.identity.user-profile-changed.v1",
                    JsonSerializer.Serialize(new
                    {
                        userId = changed.UserId,
                        publicProfileRevision = changed.ProfileRevision,
                        occurredAt = changed.OccurredAt,
                    })),
                UserSecurityStampChanged changed => (
                    "vtt.identity.user-security-stamp-changed.v1",
                    JsonSerializer.Serialize(new
                    {
                        userId = changed.UserId,
                        stampRevision = changed.StampRevision,
                        occurredAt = changed.OccurredAt,
                    })),
                UserDeactivationRequested requested => (
                    "vtt.identity.user-deactivation-requested.v1",
                    JsonSerializer.Serialize(new
                    {
                        userId = requested.UserId,
                        occurredAt = requested.OccurredAt,
                    })),
                _ => (string.Empty, string.Empty),
            };

            if (subject.Length == 0)
            {
                continue;
            }

            dbContext.IntegrationOutbox.Add(new IdentityIntegrationEventRecord
            {
                Id = Guid.CreateVersion7(),
                Subject = subject,
                Payload = payload,
                OccurredAt = domainEvent.OccurredAt,
            });
        }
    }

    public void AppendSecurityAudit(
        Guid? userId,
        Guid? sessionId,
        string eventType,
        DateTimeOffset occurredAt) =>
        dbContext.SecurityAudit.Add(new SecurityAuditRecord
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            SessionId = sessionId,
            EventType = eventType,
            OccurredAt = occurredAt,
        });

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);

    private void EnqueueEmail(
        Guid userId,
        string email,
        string template,
        string rawChallengeToken,
        DateTimeOffset expiresAt)
    {
        var payload = JsonSerializer.Serialize(new EmailPayload(
            email,
            template,
            rawChallengeToken,
            expiresAt));
        dbContext.EmailOutbox.Add(new EmailOutboxRecord
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            ProtectedPayload = _emailProtector.Protect(payload),
            CreatedAt = DateTimeOffset.UtcNow,
            NextAttemptAt = DateTimeOffset.UtcNow,
        });
    }

    internal sealed record EmailPayload(
        string Recipient,
        string Template,
        string ChallengeToken,
        DateTimeOffset ExpiresAt);
}

public sealed class AspNetPasswordCredentialService : IPasswordCredentialService
{
    private readonly PasswordHasher<PasswordSubject> _hasher = new();
    private readonly string _dummyHash;

    public AspNetPasswordCredentialService() =>
        _dummyHash = _hasher.HashPassword(new PasswordSubject(Guid.Empty), "Dummy-password-42!");

    public string Hash(Guid userId, string password) =>
        _hasher.HashPassword(new PasswordSubject(userId), password);

    public bool Verify(Guid userId, string passwordHash, string password) =>
        _hasher.VerifyHashedPassword(new PasswordSubject(userId), passwordHash, password)
            is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded;

    public void BurnVerificationTime(string password) =>
        _ = _hasher.VerifyHashedPassword(new PasswordSubject(Guid.Empty), _dummyHash, password);

    private sealed record PasswordSubject(Guid UserId);
}

public sealed class CryptographicIdentitySecretService : IIdentitySecretService
{
    public string Generate()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public string Hash(string secret) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));

    public bool Verify(string secret, string expectedHash)
    {
        var actual = Encoding.ASCII.GetBytes(Hash(secret));
        var expected = Encoding.ASCII.GetBytes(expectedHash);
        return actual.Length == expected.Length && CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}

public sealed class SystemIdentityClock : IIdentityClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public sealed class IdentitySchemaMigrator(IdentityDbContext dbContext)
{
    public Task ApplyAsync(CancellationToken cancellationToken) =>
        dbContext.Database.MigrateAsync(cancellationToken);
}

public sealed class IdentityEmailOptions
{
    public bool Enabled { get; init; }

    public string Host { get; init; } = "mailpit";

    public int Port { get; init; } = 1025;

    public string Sender { get; init; } = "no-reply@vtt.invalid";

    public string PublicWebBaseUrl { get; init; } = "http://127.0.0.1:55173";
}

public interface IIdentityEmailSender
{
    Task SendAsync(
        string recipient,
        string subject,
        string htmlBody,
        CancellationToken cancellationToken);
}

public sealed class MailKitIdentityEmailSender(IOptions<IdentityEmailOptions> options)
    : IIdentityEmailSender
{
    public async Task SendAsync(
        string recipient,
        string subject,
        string htmlBody,
        CancellationToken cancellationToken)
    {
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(options.Value.Sender));
        message.To.Add(MailboxAddress.Parse(recipient));
        message.Subject = subject;
        message.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

        using var client = new SmtpClient();
        await client.ConnectAsync(
            options.Value.Host,
            options.Value.Port,
            MailKit.Security.SecureSocketOptions.None,
            cancellationToken);
        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }
}

public sealed class IdentityEmailOutboxWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<IdentityEmailOptions> options,
    ILogger<IdentityEmailOutboxWorker> logger) : BackgroundService
{
    private static readonly Action<ILogger, Exception?> EmailDeliveryDisabled =
        LoggerMessage.Define(LogLevel.Information, new EventId(1001, nameof(EmailDeliveryDisabled)),
            "Identity email delivery is disabled.");

    private static readonly Action<ILogger, Guid, string?, Exception?> EmailDeliveryFailed =
        LoggerMessage.Define<Guid, string?>(LogLevel.Warning, new EventId(1002, nameof(EmailDeliveryFailed)),
            "Identity email delivery failed for outbox message {MessageId} with {ErrorCode}.");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            EmailDeliveryDisabled(logger, null);
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            await DeliverBatchAsync(stoppingToken);
            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
    }

    private async Task DeliverBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var sender = scope.ServiceProvider.GetRequiredService<IIdentityEmailSender>();
        var protector = scope.ServiceProvider
            .GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("Vtt.Identity.EmailOutbox.v1");
        var now = DateTimeOffset.UtcNow;
        var messages = await dbContext.EmailOutbox
            .Where(message => message.SentAt == null && message.NextAttemptAt <= now)
            .OrderBy(message => message.CreatedAt)
            .Take(20)
            .ToArrayAsync(cancellationToken);

        foreach (var message in messages)
        {
            try
            {
                var payload = JsonSerializer.Deserialize<EfIdentityStore.EmailPayload>(
                    protector.Unprotect(message.ProtectedPayload))
                    ?? throw new InvalidOperationException("Email outbox payload is empty.");
                var verification = payload.Template == "verify-email";
                var path = verification ? "/verify-email" : "/reset-password";
                var link = $"{options.Value.PublicWebBaseUrl.TrimEnd('/')}{path}?token={Uri.EscapeDataString(payload.ChallengeToken)}";
                var title = verification ? "Подтвердите email" : "Восстановление пароля";
                var body = $"<h1>{title}</h1><p><a href=\"{System.Net.WebUtility.HtmlEncode(link)}\">Продолжить</a></p>";
                await sender.SendAsync(payload.Recipient, title, body, cancellationToken);
                message.SentAt = DateTimeOffset.UtcNow;
                message.LastErrorCode = null;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                message.AttemptCount++;
                message.NextAttemptAt = DateTimeOffset.UtcNow.AddSeconds(
                    Math.Min(300, Math.Pow(2, message.AttemptCount)));
                message.LastErrorCode = exception.GetType().Name[..Math.Min(
                    exception.GetType().Name.Length,
                    64)];
                EmailDeliveryFailed(logger, message.Id, message.LastErrorCode, exception);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}

public static class IdentityInfrastructureExtensions
{
    public static IServiceCollection AddIdentityInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration["VTT_IDENTITY_CONNECTION_STRING"] ??
            configuration.GetConnectionString("Identity") ??
            "Host=localhost;Port=5432;Database=vtt_identity;Username=vtt_identity;Password=local-only-service-db";

        services.AddDbContext<IdentityDbContext>(options =>
        {
            options.UseNpgsql(connectionString);
            options.UseOpenIddict<Guid>();
        });
        var dataProtection = services.AddDataProtection().SetApplicationName("Vtt.Identity");
        var dataProtectionPath = configuration["Identity:DataProtectionPath"];
        if (string.IsNullOrWhiteSpace(dataProtectionPath))
        {
            dataProtection.UseEphemeralDataProtectionProvider();
        }
        else
        {
            dataProtection.PersistKeysToFileSystem(new DirectoryInfo(dataProtectionPath));
        }
        services.AddScoped<IIdentityStore, EfIdentityStore>();
        services.AddSingleton<IPasswordCredentialService, AspNetPasswordCredentialService>();
        services.AddSingleton<IIdentitySecretService, CryptographicIdentitySecretService>();
        services.AddSingleton<IIdentityClock, SystemIdentityClock>();
        services.AddSingleton(IdentityPolicies.Default);
        services.AddScoped<IdentityAccessService>();
        services.AddScoped<IdentitySchemaMigrator>();
        services.Configure<IdentityEmailOptions>(configuration.GetSection("Identity:Email"));
        services.AddScoped<IIdentityEmailSender, MailKitIdentityEmailSender>();
        services.AddHostedService<IdentityEmailOutboxWorker>();
        return services;
    }
}
