namespace Vtt.Campaign.Domain;

public enum CampaignStatus
{
    Draft,
    Active,
    Archived,
}

public enum CampaignRole
{
    Owner,
    CoGm,
    Player,
    Observer,
}

public enum MembershipStatus
{
    Active,
    Suspended,
    Left,
    Removed,
}

public static class CampaignCapabilities
{
    public const string Read = "campaign.read";
    public const string Update = "campaign.update";
    public const string Activate = "campaign.activate";
    public const string Archive = "campaign.archive";
    public const string Restore = "campaign.restore";
    public const string MembersRead = "members.read";
    public const string MembersManage = "members.manage";
    public const string InvitationsManage = "invitations.manage";
    public const string OwnershipTransfer = "ownership.transfer";
    public const string GameplayWrite = "gameplay.write";

    private static readonly Dictionary<CampaignRole, IReadOnlySet<string>> Matrix =
        new Dictionary<CampaignRole, IReadOnlySet<string>>
        {
            [CampaignRole.Owner] = new HashSet<string>(StringComparer.Ordinal)
            {
                Read, Update, Activate, Archive, Restore, MembersRead, MembersManage,
                InvitationsManage, OwnershipTransfer, GameplayWrite,
            },
            [CampaignRole.CoGm] = new HashSet<string>(StringComparer.Ordinal)
            {
                Read, Update, Activate, Archive, Restore, MembersRead, MembersManage,
                InvitationsManage, GameplayWrite,
            },
            [CampaignRole.Player] = new HashSet<string>(StringComparer.Ordinal)
            {
                Read, MembersRead, GameplayWrite,
            },
            [CampaignRole.Observer] = new HashSet<string>(StringComparer.Ordinal)
            {
                Read, MembersRead,
            },
        };

    public static IReadOnlySet<string> For(CampaignRole role) => Matrix[role];

    public static bool Allows(CampaignRole role, string capability) => For(role).Contains(capability);
}

public sealed class CampaignAggregate
{
    public const string PublishedRulesetFixture = "dnd5e-srd@1.0.0";

    private CampaignAggregate() { }

    public Guid Id { get; private set; }
    public Guid OwnerId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string Locale { get; private set; } = "ru-RU";
    public string TimeZone { get; private set; } = "Europe/Moscow";
    public string? RulesetVersionId { get; private set; }
    public CampaignStatus Status { get; private set; }
    public string AutomationLevel { get; private set; } = "Assisted";
    public string DicePolicy { get; private set; } = "ServerAuthoritative";
    public long Version { get; private set; }
    public long PolicyRevision { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static CampaignAggregate Create(
        Guid ownerId,
        string name,
        string locale,
        string timeZone,
        string? rulesetVersionId,
        DateTimeOffset now)
    {
        ValidateName(name);
        ValidateLocale(locale, timeZone);
        return new CampaignAggregate
        {
            Id = Guid.CreateVersion7(),
            OwnerId = ownerId,
            Name = name.Trim(),
            Locale = locale.Trim(),
            TimeZone = timeZone.Trim(),
            RulesetVersionId = NormalizeOptional(rulesetVersionId),
            Status = CampaignStatus.Draft,
            Version = 1,
            PolicyRevision = 1,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void UpdateMetadata(string name, string? description, string locale, string timeZone, long expectedVersion, DateTimeOffset now)
    {
        EnsureVersion(expectedVersion);
        ValidateName(name);
        ValidateLocale(locale, timeZone);
        Name = name.Trim();
        Description = (description ?? string.Empty).Trim();
        Locale = locale.Trim();
        TimeZone = timeZone.Trim();
        Touch(now, policyChanged: false);
    }

    public void UpdateSettings(string automationLevel, string dicePolicy, long expectedVersion, DateTimeOffset now)
    {
        EnsureVersion(expectedVersion);
        if (automationLevel is not ("Manual" or "Assisted" or "Automatic") ||
            dicePolicy is not ("ServerAuthoritative" or "GmAuthoritative"))
        {
            throw new CampaignDomainException("campaign.settings_invalid");
        }

        AutomationLevel = automationLevel;
        DicePolicy = dicePolicy;
        Touch(now, policyChanged: false);
    }

    public void Activate(long expectedVersion, DateTimeOffset now)
    {
        EnsureVersion(expectedVersion);
        if (Status != CampaignStatus.Draft)
        {
            throw new CampaignDomainException("campaign.lifecycle_invalid");
        }

        if (!string.Equals(RulesetVersionId, PublishedRulesetFixture, StringComparison.Ordinal))
        {
            throw new CampaignDomainException("campaign.ruleset_not_published");
        }

        Status = CampaignStatus.Active;
        Touch(now, policyChanged: true);
    }

    public void Archive(long expectedVersion, DateTimeOffset now)
    {
        EnsureVersion(expectedVersion);
        if (Status is not (CampaignStatus.Active or CampaignStatus.Draft))
        {
            throw new CampaignDomainException("campaign.lifecycle_invalid");
        }

        Status = CampaignStatus.Archived;
        Touch(now, policyChanged: true);
    }

    public void Restore(long expectedVersion, DateTimeOffset now)
    {
        EnsureVersion(expectedVersion);
        if (Status != CampaignStatus.Archived)
        {
            throw new CampaignDomainException("campaign.lifecycle_invalid");
        }

        Status = CampaignStatus.Draft;
        Touch(now, policyChanged: true);
    }

    public void ChangePolicy(DateTimeOffset now) => Touch(now, policyChanged: true);

    public void TransferOwnership(Guid newOwnerId, DateTimeOffset now)
    {
        if (newOwnerId == OwnerId)
        {
            throw new CampaignDomainException("campaign.owner_unchanged");
        }

        OwnerId = newOwnerId;
        Touch(now, policyChanged: true);
    }

    private void EnsureVersion(long expectedVersion)
    {
        if (expectedVersion != Version)
        {
            throw new CampaignConcurrencyException(Version);
        }
    }

    private void Touch(DateTimeOffset now, bool policyChanged)
    {
        Version++;
        if (policyChanged)
        {
            PolicyRevision++;
        }

        UpdatedAt = now;
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length is < 2 or > 120)
        {
            throw new CampaignDomainException("campaign.name_invalid");
        }
    }

    private static void ValidateLocale(string locale, string timeZone)
    {
        if (string.IsNullOrWhiteSpace(locale) || locale.Length > 20 ||
            string.IsNullOrWhiteSpace(timeZone) || timeZone.Length > 100)
        {
            throw new CampaignDomainException("campaign.locale_invalid");
        }
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class Membership
{
    private Membership() { }

    public Guid Id { get; private set; }
    public Guid CampaignId { get; private set; }
    public Guid UserId { get; private set; }
    public CampaignRole Role { get; private set; }
    public MembershipStatus Status { get; private set; }
    public long Version { get; private set; }
    public DateTimeOffset JoinedAt { get; private set; }
    public DateTimeOffset? ChangedAt { get; private set; }

    public static Membership Join(Guid campaignId, Guid userId, CampaignRole role, DateTimeOffset now)
    {
        if (role == CampaignRole.Owner)
        {
            throw new CampaignDomainException("campaign.owner_invite_forbidden");
        }

        return New(campaignId, userId, role, now);
    }

    public static Membership CreateOwner(Guid campaignId, Guid userId, DateTimeOffset now) =>
        New(campaignId, userId, CampaignRole.Owner, now);

    public void Change(CampaignRole role, MembershipStatus status, long expectedVersion, DateTimeOffset now)
    {
        if (expectedVersion != Version)
        {
            throw new CampaignConcurrencyException(Version);
        }

        if (Role == CampaignRole.Owner && (role != CampaignRole.Owner || status != MembershipStatus.Active))
        {
            throw new CampaignDomainException("campaign.last_owner_required");
        }

        if (role == CampaignRole.Owner && Role != CampaignRole.Owner)
        {
            throw new CampaignDomainException("campaign.owner_transfer_required");
        }

        Role = role;
        Status = status;
        Version++;
        ChangedAt = now;
    }

    public void ApplyOwnershipRole(CampaignRole role, DateTimeOffset now)
    {
        Role = role;
        Status = MembershipStatus.Active;
        Version++;
        ChangedAt = now;
    }

    private static Membership New(Guid campaignId, Guid userId, CampaignRole role, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(),
        CampaignId = campaignId,
        UserId = userId,
        Role = role,
        Status = MembershipStatus.Active,
        Version = 1,
        JoinedAt = now,
    };
}

public sealed class Invitation
{
    private Invitation() { }

    public Guid Id { get; private set; }
    public Guid CampaignId { get; private set; }
    public Guid CreatedBy { get; private set; }
    public CampaignRole Role { get; private set; }
    public byte[] TokenHash { get; private set; } = [];
    public DateTimeOffset ExpiresAt { get; private set; }
    public int MaxUses { get; private set; }
    public int UseCount { get; private set; }
    public bool IsRevoked { get; private set; }
    public long Version { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static Invitation Create(Guid campaignId, Guid creatorId, CampaignRole role, byte[] tokenHash, DateTimeOffset expiresAt, int maxUses, DateTimeOffset now)
    {
        if (role == CampaignRole.Owner)
        {
            throw new CampaignDomainException("campaign.owner_invite_forbidden");
        }

        if (expiresAt <= now || expiresAt > now.AddDays(30) || maxUses is < 1 or > 100)
        {
            throw new CampaignDomainException("campaign.invitation_invalid");
        }

        return new Invitation
        {
            Id = Guid.CreateVersion7(),
            CampaignId = campaignId,
            CreatedBy = creatorId,
            Role = role,
            TokenHash = tokenHash,
            ExpiresAt = expiresAt,
            MaxUses = maxUses,
            Version = 1,
            CreatedAt = now,
        };
    }

    public bool IsUsable(DateTimeOffset now) => !IsRevoked && ExpiresAt > now && UseCount < MaxUses;

    public void Consume(DateTimeOffset now)
    {
        if (!IsUsable(now))
        {
            throw new CampaignDomainException("campaign.invitation_invalid");
        }

        UseCount++;
        Version++;
    }

    public void Revoke(long expectedVersion)
    {
        if (expectedVersion != Version)
        {
            throw new CampaignConcurrencyException(Version);
        }

        IsRevoked = true;
        Version++;
    }
}

public sealed class OwnershipTransfer
{
    private OwnershipTransfer() { }
    public Guid Id { get; private set; }
    public Guid CampaignId { get; private set; }
    public Guid FromUserId { get; private set; }
    public Guid ToUserId { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? AcceptedAt { get; private set; }

    public static OwnershipTransfer Request(Guid campaignId, Guid from, Guid to, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(),
        CampaignId = campaignId,
        FromUserId = from,
        ToUserId = to,
        ExpiresAt = now.AddDays(2),
    };

    public void Accept(Guid userId, DateTimeOffset now)
    {
        if (userId != ToUserId || AcceptedAt is not null || ExpiresAt <= now)
        {
            throw new CampaignDomainException("campaign.transfer_invalid");
        }

        AcceptedAt = now;
    }
}

public sealed class CampaignDomainException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

public sealed class CampaignConcurrencyException(long currentVersion) : Exception("campaign.version_conflict")
{
    public long CurrentVersion { get; } = currentVersion;
}
