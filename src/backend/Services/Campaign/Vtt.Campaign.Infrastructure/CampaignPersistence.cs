using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Vtt.Campaign.Application;
using Vtt.Campaign.Domain;
using Vtt.Messaging;
using Vtt.Messaging.Nats;

namespace Vtt.Campaign.Infrastructure;

public sealed class CampaignDbContext(DbContextOptions<CampaignDbContext> options) : DbContext(options)
{
    public DbSet<CampaignAggregate> Campaigns => Set<CampaignAggregate>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<Invitation> Invitations => Set<Invitation>();
    public DbSet<OwnershipTransfer> OwnershipTransfers => Set<OwnershipTransfer>();
    public DbSet<CampaignAuditRecord> Audit => Set<CampaignAuditRecord>();
    public DbSet<CampaignIntegrationEventRecord> IntegrationOutbox => Set<CampaignIntegrationEventRecord>();
    public DbSet<CampaignDomainEventRecord> DomainEvents => Set<CampaignDomainEventRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CampaignAggregate>(entity =>
        {
            entity.ToTable("campaigns");
            entity.HasKey(campaign => campaign.Id);
            entity.Property(campaign => campaign.Name).HasMaxLength(120);
            entity.Property(campaign => campaign.Description).HasMaxLength(4000);
            entity.Property(campaign => campaign.Locale).HasMaxLength(20);
            entity.Property(campaign => campaign.TimeZone).HasMaxLength(100);
            entity.Property(campaign => campaign.RulesetVersionId).HasMaxLength(100);
            entity.Property(campaign => campaign.AutomationLevel).HasMaxLength(32);
            entity.Property(campaign => campaign.DicePolicy).HasMaxLength(32);
            entity.Property(campaign => campaign.Version).IsConcurrencyToken();
        });

        modelBuilder.Entity<Membership>(entity =>
        {
            entity.ToTable("memberships");
            entity.HasKey(member => member.Id);
            entity.HasIndex(member => new { member.CampaignId, member.UserId }).IsUnique();
            entity.HasIndex(member => member.UserId);
            entity.Property(member => member.Version).IsConcurrencyToken();
        });

        modelBuilder.Entity<Invitation>(entity =>
        {
            entity.ToTable("invitations");
            entity.HasKey(invitation => invitation.Id);
            entity.HasIndex(invitation => invitation.TokenHash).IsUnique();
            entity.Property(invitation => invitation.TokenHash).HasMaxLength(32);
            entity.Property(invitation => invitation.Version).IsConcurrencyToken();
        });

        modelBuilder.Entity<OwnershipTransfer>(entity =>
        {
            entity.ToTable("ownership_transfers");
            entity.HasKey(transfer => transfer.Id);
            entity.HasIndex(transfer => transfer.CampaignId);
        });

        modelBuilder.Entity<CampaignAuditRecord>(entity =>
        {
            entity.ToTable("campaign_audit");
            entity.HasKey(record => record.Id);
            entity.Property(record => record.Action).HasMaxLength(100);
            entity.Property(record => record.Target).HasMaxLength(200);
            entity.HasIndex(record => new { record.CampaignId, record.OccurredAt });
        });

        modelBuilder.Entity<CampaignIntegrationEventRecord>(entity =>
        {
            entity.ToTable("integration_outbox");
            entity.HasKey(record => record.Id);
            entity.Property(record => record.EventType).HasMaxLength(150);
            entity.Property(record => record.Payload).HasColumnType("jsonb");
            entity.Property(record => record.LastPublishError).HasMaxLength(1000);
            entity.HasIndex(record => new { record.CampaignId, record.PolicyRevision });
            entity.HasIndex(record => new { record.PublishedAt, record.OccurredAt });
        });

        modelBuilder.Entity<CampaignDomainEventRecord>(entity =>
        {
            entity.ToTable("domain_events");
            entity.HasKey(record => record.Id);
            entity.Property(record => record.EventType).HasMaxLength(150);
            entity.Property(record => record.Payload).HasColumnType("jsonb");
            entity.HasIndex(record => new { record.StreamId, record.StreamVersion }).IsUnique();
        });
    }
}

public sealed class EfCampaignStore(CampaignDbContext dbContext) : ICampaignStore
{
    public Task<CampaignAggregate?> FindCampaignAsync(Guid campaignId, CancellationToken cancellationToken) =>
        dbContext.Campaigns.SingleOrDefaultAsync(campaign => campaign.Id == campaignId, cancellationToken);

    public Task<Membership?> FindMembershipAsync(Guid campaignId, Guid userId, CancellationToken cancellationToken) =>
        dbContext.Memberships.SingleOrDefaultAsync(member => member.CampaignId == campaignId && member.UserId == userId, cancellationToken);

    public Task<Membership?> FindMembershipByIdAsync(Guid campaignId, Guid membershipId, CancellationToken cancellationToken) =>
        dbContext.Memberships.SingleOrDefaultAsync(member => member.CampaignId == campaignId && member.Id == membershipId, cancellationToken);

    public async Task<IReadOnlyList<CampaignAggregate>> ListCampaignsAsync(Guid userId, CancellationToken cancellationToken) =>
        await (from campaign in dbContext.Campaigns
               join member in dbContext.Memberships on campaign.Id equals member.CampaignId
               where member.UserId == userId && member.Status == MembershipStatus.Active
               orderby campaign.UpdatedAt descending
               select campaign).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Membership>> ListMembersAsync(Guid campaignId, CancellationToken cancellationToken) =>
        await dbContext.Memberships.Where(member => member.CampaignId == campaignId).OrderBy(member => member.JoinedAt).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Invitation>> ListInvitationsAsync(Guid campaignId, CancellationToken cancellationToken) =>
        await dbContext.Invitations.Where(invitation => invitation.CampaignId == campaignId).OrderByDescending(invitation => invitation.CreatedAt).ToListAsync(cancellationToken);

    public Task<Invitation?> FindInvitationAsync(Guid campaignId, Guid invitationId, CancellationToken cancellationToken) =>
        dbContext.Invitations.SingleOrDefaultAsync(invitation => invitation.CampaignId == campaignId && invitation.Id == invitationId, cancellationToken);

    public Task<Invitation?> FindInvitationByTokenHashAsync(byte[] tokenHash, CancellationToken cancellationToken) =>
        dbContext.Invitations.SingleOrDefaultAsync(invitation => invitation.TokenHash.SequenceEqual(tokenHash), cancellationToken);

    public Task<OwnershipTransfer?> FindPendingTransferAsync(Guid campaignId, CancellationToken cancellationToken) =>
        dbContext.OwnershipTransfers.SingleOrDefaultAsync(transfer => transfer.CampaignId == campaignId && transfer.AcceptedAt == null, cancellationToken);

    public void AddCampaign(CampaignAggregate campaign) => dbContext.Campaigns.Add(campaign);
    public void AddMembership(Membership membership) => dbContext.Memberships.Add(membership);
    public void AddInvitation(Invitation invitation) => dbContext.Invitations.Add(invitation);
    public void AddTransfer(OwnershipTransfer transfer) => dbContext.OwnershipTransfers.Add(transfer);
    public void AddAudit(CampaignAuditRecord audit) => dbContext.Audit.Add(audit);
    public void AddIntegrationEvent(CampaignIntegrationEventRecord integrationEvent) => dbContext.IntegrationOutbox.Add(integrationEvent);
    public void AddDomainEvent(CampaignDomainEventRecord domainEvent) => dbContext.DomainEvents.Add(domainEvent);

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new CampaignStoreConcurrencyException { Source = exception.Source };
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new CampaignStoreConcurrencyException { Source = exception.Source };
        }
        catch (Exception exception) when (IsPostgresConcurrency(exception))
        {
            throw new CampaignStoreConcurrencyException { Source = exception.Source };
        }
    }

    public async Task ExecuteSerializableAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            await operation(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception exception) when (IsPostgresConcurrency(exception))
        {
            throw new CampaignStoreConcurrencyException { Source = exception.Source };
        }
    }

    private static bool IsPostgresConcurrency(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.UniqueViolation })
            {
                return true;
            }
        }

        return false;
    }
}

public static class CampaignInfrastructureExtensions
{
    public static IServiceCollection AddCampaignInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("campaign")
            ?? "Host=localhost;Port=55432;Database=vtt_campaign;Username=vtt;Password=vtt_local_only";
        services.AddDbContext<CampaignDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<ICampaignStore, EfCampaignStore>();
        services.AddVttNatsMessaging(new NatsMessagingOptions
        {
            Url = configuration["NATS_URL"] ?? "nats://127.0.0.1:54222",
            ClientName = "campaign",
        });
        services.AddHostedService<CampaignOutboxRelay>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(new CampaignPolicySigner(
            configuration["Campaign:PolicySigningKey"] ?? "local-development-policy-signing-key-change-me"));
        services.AddScoped<CampaignAccessService>();
        return services;
    }
}

public sealed partial class CampaignOutboxRelay(
    IServiceScopeFactory scopeFactory,
    IIntegrationEventPublisher publisher,
    TimeProvider timeProvider,
    ILogger<CampaignOutboxRelay> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var published = false;
            try
            {
                published = await PublishNextAsync(stoppingToken);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                LogRelayFailure(logger, exception.GetType().Name);
            }

            if (!published)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250), timeProvider, stoppingToken);
            }
        }
    }

    internal async Task<bool> PublishNextAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CampaignDbContext>();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var record = await dbContext.IntegrationOutbox
            .FromSqlRaw("SELECT * FROM integration_outbox WHERE \"PublishedAt\" IS NULL ORDER BY \"OccurredAt\" FOR UPDATE SKIP LOCKED LIMIT 1")
            .SingleOrDefaultAsync(cancellationToken);
        if (record is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return false;
        }

        try
        {
            var data = JsonSerializer.Deserialize<JsonElement>(record.Payload);
            var envelope = new EventEnvelope(
                record.Id,
                record.EventType,
                record.OccurredAt,
                "campaign",
                new AggregateReference("campaign", record.CampaignId, record.PolicyRevision),
                record.CampaignId.ToString("D"),
                null,
                record.Id.ToString("D"),
                record.Id.ToString("D"),
                1,
                null,
                data,
                new Dictionary<string, string> { ["policyRevision"] = record.PolicyRevision.ToString(System.Globalization.CultureInfo.InvariantCulture) });
            await publisher.PublishAsync(
                new IntegrationMessage(record.Id, SubjectFor(record.EventType), EventEnvelopeJson.Serialize(envelope), null),
                cancellationToken);
            record.PublishedAt = timeProvider.GetUtcNow();
            record.LastPublishError = null;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            record.PublishAttempts++;
            record.LastPublishError = exception.GetType().Name;
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            LogPublishFailure(logger, record.Id, record.PublishAttempts, exception.GetType().Name);
            return false;
        }

        record.PublishAttempts++;
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public static string SubjectFor(string eventType)
    {
        var eventName = eventType.EndsWith(".v1", StringComparison.OrdinalIgnoreCase)
            ? eventType[..^3]
            : eventType;
        eventName = PascalCaseBoundary().Replace(eventName, "$1-$2").Replace('.', '-').ToLowerInvariant();
        return IntegrationSubject.Create("campaign", "campaign", eventName, 1);
    }

    [System.Text.RegularExpressions.GeneratedRegex("([a-z0-9])([A-Z])", System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex PascalCaseBoundary();

    [LoggerMessage(4201, LogLevel.Warning, "Campaign outbox relay iteration failed; error {ErrorCode}.")]
    private static partial void LogRelayFailure(ILogger logger, string errorCode);

    [LoggerMessage(4202, LogLevel.Warning, "Campaign outbox event {EventId} publish attempt {Attempt} failed; error {ErrorCode}.")]
    private static partial void LogPublishFailure(ILogger logger, Guid eventId, int attempt, string errorCode);
}

public sealed class CampaignDesignTimeDbContextFactory : IDesignTimeDbContextFactory<CampaignDbContext>
{
    public CampaignDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<CampaignDbContext>()
            .UseNpgsql("Host=localhost;Port=55432;Database=vtt_campaign;Username=vtt;Password=vtt_local_only")
            .Options;
        return new CampaignDbContext(options);
    }
}
