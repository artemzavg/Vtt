using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Vtt.Campaign.Domain;

namespace Vtt.Campaign.Application;

public interface ICampaignStore
{
    Task<CampaignAggregate?> FindCampaignAsync(Guid campaignId, CancellationToken cancellationToken);
    Task<Membership?> FindMembershipAsync(Guid campaignId, Guid userId, CancellationToken cancellationToken);
    Task<Membership?> FindMembershipByIdAsync(Guid campaignId, Guid membershipId, CancellationToken cancellationToken);
    Task<IReadOnlyList<CampaignAggregate>> ListCampaignsAsync(Guid userId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Membership>> ListMembersAsync(Guid campaignId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Invitation>> ListInvitationsAsync(Guid campaignId, CancellationToken cancellationToken);
    Task<Invitation?> FindInvitationAsync(Guid campaignId, Guid invitationId, CancellationToken cancellationToken);
    Task<Invitation?> FindInvitationByTokenHashAsync(byte[] tokenHash, CancellationToken cancellationToken);
    Task<OwnershipTransfer?> FindPendingTransferAsync(Guid campaignId, CancellationToken cancellationToken);
    void AddCampaign(CampaignAggregate campaign);
    void AddMembership(Membership membership);
    void AddInvitation(Invitation invitation);
    void AddTransfer(OwnershipTransfer transfer);
    void AddAudit(CampaignAuditRecord audit);
    void AddIntegrationEvent(CampaignIntegrationEventRecord integrationEvent);
    void AddDomainEvent(CampaignDomainEventRecord domainEvent);
    Task SaveChangesAsync(CancellationToken cancellationToken);
    Task ExecuteSerializableAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken);
}

public sealed record CampaignAuditRecord(
    Guid Id,
    Guid CampaignId,
    Guid ActorId,
    string Action,
    string Target,
    DateTimeOffset OccurredAt,
    long PolicyRevision);

public sealed record CampaignIntegrationEventRecord(
    Guid Id,
    string EventType,
    Guid CampaignId,
    long PolicyRevision,
    string Payload,
    DateTimeOffset OccurredAt)
{
    public DateTimeOffset? PublishedAt { get; set; }

    public int PublishAttempts { get; set; }

    public string? LastPublishError { get; set; }
}

public sealed record CampaignDomainEventRecord(
    Guid Id,
    Guid StreamId,
    long StreamVersion,
    string EventType,
    string Payload,
    DateTimeOffset OccurredAt);

public sealed class CampaignStoreConcurrencyException : Exception;

public sealed class CampaignAccessService(
    ICampaignStore store,
    TimeProvider timeProvider,
    CampaignPolicySigner signer)
{
    public async Task<CampaignResponse> CreateAsync(Guid actorId, CreateCampaignRequest request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var campaign = CampaignAggregate.Create(actorId, request.Name, request.Locale, request.TimeZone, request.RulesetVersionId, now);
        var owner = Membership.CreateOwner(campaign.Id, actorId, now);
        store.AddCampaign(campaign);
        store.AddMembership(owner);
        Record(campaign, actorId, "campaign.created", campaign.Id.ToString("D"), "CampaignCreated.v1",
            new
            {
                campaignId = campaign.Id,
                ownerId = actorId,
                campaign.Name,
                campaign.Locale,
                campaign.TimeZone,
                campaign.RulesetVersionId,
                campaign.Status,
            });
        await store.SaveChangesAsync(cancellationToken);
        return ToResponse(campaign, owner);
    }

    public async Task<IReadOnlyList<CampaignResponse>> ListAsync(Guid actorId, CancellationToken cancellationToken)
    {
        var campaigns = await store.ListCampaignsAsync(actorId, cancellationToken);
        var result = new List<CampaignResponse>(campaigns.Count);
        foreach (var campaign in campaigns)
        {
            var membership = await store.FindMembershipAsync(campaign.Id, actorId, cancellationToken);
            if (membership is { Status: MembershipStatus.Active })
            {
                result.Add(ToResponse(campaign, membership));
            }
        }

        return result;
    }

    public async Task<CampaignResponse> GetAsync(Guid actorId, Guid campaignId, CancellationToken cancellationToken)
    {
        var (campaign, membership) = await RequireCapabilityAsync(actorId, campaignId, CampaignCapabilities.Read, cancellationToken);
        return ToResponse(campaign, membership);
    }

    public async Task<CampaignResponse> UpdateAsync(Guid actorId, Guid campaignId, UpdateCampaignRequest request, long expectedVersion, CancellationToken cancellationToken)
    {
        var (campaign, member) = await RequireCapabilityAsync(actorId, campaignId, CampaignCapabilities.Update, cancellationToken);
        campaign.UpdateMetadata(request.Name, request.Description, request.Locale, request.TimeZone, expectedVersion, timeProvider.GetUtcNow());
        Record(campaign, actorId, "campaign.updated", campaignId.ToString("D"), "CampaignMetadataChanged.v1",
            new { campaignId, campaign.Name, campaign.Description, campaign.Locale, campaign.TimeZone });
        await SaveAsync(cancellationToken);
        return ToResponse(campaign, member);
    }

    public async Task<CampaignResponse> UpdateSettingsAsync(Guid actorId, Guid campaignId, UpdateCampaignSettingsRequest request, long expectedVersion, CancellationToken cancellationToken)
    {
        var (campaign, member) = await RequireCapabilityAsync(actorId, campaignId, CampaignCapabilities.Update, cancellationToken);
        campaign.UpdateSettings(request.AutomationLevel, request.DicePolicy, expectedVersion, timeProvider.GetUtcNow());
        Record(campaign, actorId, "campaign.settings_changed", campaignId.ToString("D"), "CampaignSettingsChanged.v1",
            new { campaignId, campaign.AutomationLevel, campaign.DicePolicy });
        await SaveAsync(cancellationToken);
        return ToResponse(campaign, member);
    }

    public Task<CampaignResponse> ActivateAsync(Guid actorId, Guid campaignId, long expectedVersion, CancellationToken cancellationToken) =>
        ChangeLifecycleAsync(actorId, campaignId, CampaignCapabilities.Activate, expectedVersion, "campaign.activated", (campaign, now) => campaign.Activate(expectedVersion, now), cancellationToken);

    public Task<CampaignResponse> ArchiveAsync(Guid actorId, Guid campaignId, long expectedVersion, CancellationToken cancellationToken) =>
        ChangeLifecycleAsync(actorId, campaignId, CampaignCapabilities.Archive, expectedVersion, "campaign.archived", (campaign, now) => campaign.Archive(expectedVersion, now), cancellationToken);

    public Task<CampaignResponse> RestoreAsync(Guid actorId, Guid campaignId, long expectedVersion, CancellationToken cancellationToken) =>
        ChangeLifecycleAsync(actorId, campaignId, CampaignCapabilities.Restore, expectedVersion, "campaign.restored", (campaign, now) => campaign.Restore(expectedVersion, now), cancellationToken);

    public async Task<IReadOnlyList<MembershipResponse>> ListMembersAsync(Guid actorId, Guid campaignId, CancellationToken cancellationToken)
    {
        await RequireCapabilityAsync(actorId, campaignId, CampaignCapabilities.MembersRead, cancellationToken);
        return (await store.ListMembersAsync(campaignId, cancellationToken)).Select(ToResponse).ToArray();
    }

    public async Task<MembershipResponse> ChangeMemberAsync(Guid actorId, Guid campaignId, Guid membershipId, ChangeMembershipRequest request, long expectedVersion, CancellationToken cancellationToken)
    {
        var (campaign, _) = await RequireCapabilityAsync(actorId, campaignId, CampaignCapabilities.MembersManage, cancellationToken);
        var target = await store.FindMembershipByIdAsync(campaignId, membershipId, cancellationToken) ?? throw new CampaignNotFoundException();
        if (!Enum.TryParse<CampaignRole>(request.Role, true, out var role) ||
            !Enum.TryParse<MembershipStatus>(request.Status, true, out var status))
        {
            throw new CampaignDomainException("campaign.membership_invalid");
        }

        target.Change(role, status, expectedVersion, timeProvider.GetUtcNow());
        campaign.ChangePolicy(timeProvider.GetUtcNow());
        Record(campaign, actorId, "membership.changed", target.UserId.ToString("D"), "CampaignMembershipChanged.v1", new { campaignId, subjectId = target.UserId, role, status });
        await SaveAsync(cancellationToken);
        return ToResponse(target);
    }

    public Task<MembershipResponse> RemoveMemberAsync(Guid actorId, Guid campaignId, Guid membershipId, long expectedVersion, CancellationToken cancellationToken) =>
        ChangeMemberAsync(actorId, campaignId, membershipId, new ChangeMembershipRequest(CampaignRole.Player.ToString(), MembershipStatus.Removed.ToString()), expectedVersion, cancellationToken);

    public async Task<InvitationCreatedResponse> CreateInvitationAsync(Guid actorId, Guid campaignId, CreateInvitationRequest request, CancellationToken cancellationToken)
    {
        var (campaign, _) = await RequireCapabilityAsync(actorId, campaignId, CampaignCapabilities.InvitationsManage, cancellationToken);
        if (!Enum.TryParse<CampaignRole>(request.Role, true, out var role))
        {
            throw new CampaignDomainException("campaign.invitation_invalid");
        }

        var rawToken = Base64Url(RandomNumberGenerator.GetBytes(32));
        var invitation = Invitation.Create(campaignId, actorId, role, HashToken(rawToken), request.ExpiresAt, request.MaxUses, timeProvider.GetUtcNow());
        store.AddInvitation(invitation);
        campaign.ChangePolicy(timeProvider.GetUtcNow());
        Record(campaign, actorId, "invitation.created", invitation.Id.ToString("D"), "InvitationCreated.v1",
            new { campaignId, invitationId = invitation.Id, role, invitation.ExpiresAt, invitation.MaxUses, tokenHash = Convert.ToHexStringLower(invitation.TokenHash) });
        await SaveAsync(cancellationToken);
        return new InvitationCreatedResponse(invitation.Id, rawToken, invitation.Role.ToString(), invitation.ExpiresAt, invitation.MaxUses, invitation.Version);
    }

    public async Task<IReadOnlyList<InvitationResponse>> ListInvitationsAsync(Guid actorId, Guid campaignId, CancellationToken cancellationToken)
    {
        await RequireCapabilityAsync(actorId, campaignId, CampaignCapabilities.InvitationsManage, cancellationToken);
        return (await store.ListInvitationsAsync(campaignId, cancellationToken)).Select(ToResponse).ToArray();
    }

    public async Task RevokeInvitationAsync(Guid actorId, Guid campaignId, Guid invitationId, long expectedVersion, CancellationToken cancellationToken)
    {
        var (campaign, _) = await RequireCapabilityAsync(actorId, campaignId, CampaignCapabilities.InvitationsManage, cancellationToken);
        var invitation = await store.FindInvitationAsync(campaignId, invitationId, cancellationToken) ?? throw new CampaignNotFoundException();
        invitation.Revoke(expectedVersion);
        campaign.ChangePolicy(timeProvider.GetUtcNow());
        Record(campaign, actorId, "invitation.revoked", invitationId.ToString("D"), "InvitationRevoked.v1",
            new { campaignId, invitationId });
        await SaveAsync(cancellationToken);
    }

    public async Task<CampaignResponse> AcceptInvitationAsync(Guid actorId, string rawToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken) || rawToken.Length > 256)
        {
            throw new CampaignInvalidInvitationException();
        }

        CampaignResponse? response = null;
        await store.ExecuteSerializableAsync(async ct =>
        {
            var invitation = await store.FindInvitationByTokenHashAsync(HashToken(rawToken), ct);
            if (invitation is null || !invitation.IsUsable(timeProvider.GetUtcNow()))
            {
                throw new CampaignInvalidInvitationException();
            }

            var campaign = await store.FindCampaignAsync(invitation.CampaignId, ct) ?? throw new CampaignInvalidInvitationException();
            var existing = await store.FindMembershipAsync(campaign.Id, actorId, ct);
            if (existing is not null)
            {
                throw new CampaignInvalidInvitationException();
            }

            invitation.Consume(timeProvider.GetUtcNow());
            var member = Membership.Join(campaign.Id, actorId, invitation.Role, timeProvider.GetUtcNow());
            store.AddMembership(member);
            campaign.ChangePolicy(timeProvider.GetUtcNow());
            Record(campaign, actorId, "invitation.accepted", invitation.Id.ToString("D"), "CampaignMembershipChanged.v1", new { campaignId = campaign.Id, subjectId = actorId, role = invitation.Role, status = MembershipStatus.Active });
            await store.SaveChangesAsync(ct);
            response = ToResponse(campaign, member);
        }, cancellationToken);

        return response ?? throw new CampaignInvalidInvitationException();
    }

    public async Task<Guid> RequestOwnershipTransferAsync(Guid actorId, Guid campaignId, Guid targetUserId, CancellationToken cancellationToken)
    {
        var (campaign, _) = await RequireCapabilityAsync(actorId, campaignId, CampaignCapabilities.OwnershipTransfer, cancellationToken);
        var target = await store.FindMembershipAsync(campaignId, targetUserId, cancellationToken);
        if (target is not { Status: MembershipStatus.Active } || target.Role == CampaignRole.Owner)
        {
            throw new CampaignDomainException("campaign.transfer_target_invalid");
        }

        if (await store.FindPendingTransferAsync(campaignId, cancellationToken) is not null)
        {
            throw new CampaignDomainException("campaign.transfer_pending");
        }

        var transfer = OwnershipTransfer.Request(campaignId, actorId, targetUserId, timeProvider.GetUtcNow());
        store.AddTransfer(transfer);
        campaign.ChangePolicy(timeProvider.GetUtcNow());
        Record(campaign, actorId, "ownership.transfer_requested", targetUserId.ToString("D"), "CampaignOwnershipTransferRequested.v1",
            new { campaignId, transferId = transfer.Id, fromUserId = actorId, toUserId = targetUserId, transfer.ExpiresAt });
        await SaveAsync(cancellationToken);
        return transfer.Id;
    }

    public async Task<CampaignResponse> AcceptOwnershipTransferAsync(Guid actorId, Guid campaignId, CancellationToken cancellationToken)
    {
        CampaignResponse? response = null;
        await store.ExecuteSerializableAsync(async ct =>
        {
            var transfer = await store.FindPendingTransferAsync(campaignId, ct) ?? throw new CampaignDomainException("campaign.transfer_invalid");
            var campaign = await store.FindCampaignAsync(campaignId, ct) ?? throw new CampaignNotFoundException();
            var oldOwner = await store.FindMembershipAsync(campaignId, transfer.FromUserId, ct) ?? throw new CampaignDomainException("campaign.transfer_invalid");
            var newOwner = await store.FindMembershipAsync(campaignId, transfer.ToUserId, ct) ?? throw new CampaignDomainException("campaign.transfer_invalid");
            transfer.Accept(actorId, timeProvider.GetUtcNow());
            oldOwner.ApplyOwnershipRole(CampaignRole.CoGm, timeProvider.GetUtcNow());
            newOwner.ApplyOwnershipRole(CampaignRole.Owner, timeProvider.GetUtcNow());
            campaign.TransferOwnership(actorId, timeProvider.GetUtcNow());
            Record(campaign, actorId, "ownership.transferred", actorId.ToString("D"), "CampaignPolicyRevisionChanged.v1", new { campaignId, policyRevision = campaign.PolicyRevision });
            await store.SaveChangesAsync(ct);
            response = ToResponse(campaign, newOwner);
        }, cancellationToken);
        return response ?? throw new CampaignDomainException("campaign.transfer_invalid");
    }

    public async Task<AuthorizationCheckResponse> CheckAsync(Guid campaignId, Guid subjectId, IReadOnlyList<string> actions, CancellationToken cancellationToken)
    {
        var campaign = await store.FindCampaignAsync(campaignId, cancellationToken) ?? throw new CampaignNotFoundException();
        var membership = await store.FindMembershipAsync(campaignId, subjectId, cancellationToken);
        var active = membership is { Status: MembershipStatus.Active };
        var decisions = actions.Distinct(StringComparer.Ordinal).Select(action => new AuthorizationDecision(
            action,
            active && CampaignCapabilities.Allows(membership!.Role, action) && !(campaign.Status == CampaignStatus.Archived && action == CampaignCapabilities.GameplayWrite),
            active ? $"role:{membership!.Role};revision:{campaign.PolicyRevision}" : "deny:no-active-membership")).ToArray();
        return new AuthorizationCheckResponse(campaignId, subjectId, campaign.PolicyRevision, decisions);
    }

    public async Task<PolicySnapshotResponse> SnapshotAsync(Guid campaignId, CancellationToken cancellationToken)
    {
        var campaign = await store.FindCampaignAsync(campaignId, cancellationToken) ?? throw new CampaignNotFoundException();
        var members = (await store.ListMembersAsync(campaignId, cancellationToken))
            .OrderBy(member => member.UserId)
            .Select(member => new MembershipPolicyEntry(member.UserId, member.Role.ToString(), member.Status.ToString(),
                member.Status == MembershipStatus.Active ? CampaignCapabilities.For(member.Role).Order(StringComparer.Ordinal).ToArray() : []))
            .ToArray();
        return signer.Sign(campaign.Id, campaign.PolicyRevision, campaign.Status.ToString(), members);
    }

    private async Task<CampaignResponse> ChangeLifecycleAsync(Guid actorId, Guid campaignId, string capability, long expectedVersion, string action, Action<CampaignAggregate, DateTimeOffset> change, CancellationToken cancellationToken)
    {
        var (campaign, member) = await RequireCapabilityAsync(actorId, campaignId, capability, cancellationToken);
        change(campaign, timeProvider.GetUtcNow());
        Record(campaign, actorId, action, campaignId.ToString("D"), "CampaignLifecycleChanged.v1", new { campaignId, status = campaign.Status });
        await SaveAsync(cancellationToken);
        return ToResponse(campaign, member);
    }

    private async Task<(CampaignAggregate Campaign, Membership Membership)> RequireCapabilityAsync(Guid actorId, Guid campaignId, string capability, CancellationToken cancellationToken)
    {
        var campaign = await store.FindCampaignAsync(campaignId, cancellationToken) ?? throw new CampaignNotFoundException();
        var membership = await store.FindMembershipAsync(campaignId, actorId, cancellationToken);
        if (membership is not { Status: MembershipStatus.Active })
        {
            throw new CampaignNotFoundException();
        }

        if (!CampaignCapabilities.Allows(membership.Role, capability))
        {
            throw new CampaignForbiddenException();
        }

        return (campaign, membership);
    }

    private void Record(CampaignAggregate campaign, Guid actorId, string action, string target, string eventType, object payload)
    {
        Audit(campaign, actorId, action, target);
        store.AddIntegrationEvent(new CampaignIntegrationEventRecord(Guid.CreateVersion7(), eventType, campaign.Id, campaign.PolicyRevision,
            JsonSerializer.Serialize(payload), timeProvider.GetUtcNow()));
        store.AddDomainEvent(new CampaignDomainEventRecord(Guid.CreateVersion7(), campaign.Id, campaign.Version, eventType,
            JsonSerializer.Serialize(payload), timeProvider.GetUtcNow()));
    }

    private void Audit(CampaignAggregate campaign, Guid actorId, string action, string target) =>
        store.AddAudit(new CampaignAuditRecord(Guid.CreateVersion7(), campaign.Id, actorId, action, target, timeProvider.GetUtcNow(), campaign.PolicyRevision));

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await store.SaveChangesAsync(cancellationToken);
        }
        catch (CampaignStoreConcurrencyException)
        {
            throw new CampaignConcurrencyException(-1);
        }
    }

    private static byte[] HashToken(string rawToken) => SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static CampaignResponse ToResponse(CampaignAggregate campaign, Membership member) => new(
        campaign.Id, campaign.OwnerId, campaign.Name, campaign.Description, campaign.Locale, campaign.TimeZone,
        campaign.RulesetVersionId, campaign.Status.ToString(), campaign.AutomationLevel, campaign.DicePolicy,
        campaign.Version, campaign.PolicyRevision, member.Role.ToString(),
        member.Status == MembershipStatus.Active ? CampaignCapabilities.For(member.Role).Order(StringComparer.Ordinal).ToArray() : []);

    private static MembershipResponse ToResponse(Membership member) => new(member.Id, member.UserId, member.Role.ToString(), member.Status.ToString(), member.Version, member.JoinedAt);

    private InvitationResponse ToResponse(Invitation invitation) => new(
        invitation.Id, invitation.Role.ToString(), invitation.ExpiresAt, invitation.MaxUses, invitation.UseCount,
        invitation.IsRevoked ? "Revoked" : invitation.ExpiresAt <= timeProvider.GetUtcNow() ? "Expired" : invitation.UseCount >= invitation.MaxUses ? "Exhausted" : "Active",
        invitation.Version);
}

public sealed class CampaignPolicySigner
{
    private readonly byte[] _key;

    public CampaignPolicySigner(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length < 32)
        {
            throw new InvalidOperationException("Campaign policy signing key must contain at least 32 characters.");
        }

        _key = Encoding.UTF8.GetBytes(key);
    }

    public PolicySnapshotResponse Sign(Guid campaignId, long revision, string status, IReadOnlyList<MembershipPolicyEntry> members)
    {
        var canonical = JsonSerializer.Serialize(new { campaignId, revision, status, members });
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        var signature = Convert.ToBase64String(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(hash)));
        return new PolicySnapshotResponse(campaignId, revision, status, members, hash, signature);
    }
}

public sealed class CampaignNotFoundException : Exception;
public sealed class CampaignForbiddenException : Exception;
public sealed class CampaignInvalidInvitationException : Exception;
