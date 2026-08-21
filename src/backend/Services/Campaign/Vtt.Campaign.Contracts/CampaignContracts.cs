namespace Vtt.Campaign.Contracts;

public sealed record CreateCampaignRequest(
    string Name,
    string Locale,
    string TimeZone,
    string? RulesetVersionId);

public sealed record UpdateCampaignRequest(string Name, string? Description, string Locale, string TimeZone);
public sealed record UpdateCampaignSettingsRequest(string AutomationLevel, string DicePolicy);
public sealed record ChangeMembershipRequest(string Role, string Status);
public sealed record CreateInvitationRequest(string Role, DateTimeOffset ExpiresAt, int MaxUses);
public sealed record AcceptInvitationRequest(string Token);
public sealed record RequestOwnershipTransferRequest(Guid TargetUserId);
public sealed record AuthorizationCheckRequest(Guid SubjectId, Guid CampaignId, IReadOnlyList<string> Actions, long? KnownRevision);

public sealed record CampaignResponse(
    Guid CampaignId,
    Guid OwnerId,
    string Name,
    string Description,
    string Locale,
    string TimeZone,
    string? RulesetVersionId,
    string Status,
    string AutomationLevel,
    string DicePolicy,
    long Version,
    long PolicyRevision,
    string Role,
    IReadOnlyList<string> EffectiveCapabilities);

public sealed record MembershipResponse(
    Guid MembershipId,
    Guid UserId,
    string Role,
    string Status,
    long Version,
    DateTimeOffset JoinedAt);

public sealed record InvitationCreatedResponse(
    Guid InvitationId,
    string Token,
    string Role,
    DateTimeOffset ExpiresAt,
    int MaxUses,
    long Version);

public sealed record InvitationResponse(
    Guid InvitationId,
    string Role,
    DateTimeOffset ExpiresAt,
    int MaxUses,
    int UseCount,
    string Status,
    long Version);

public sealed record AuthorizationDecision(string Action, bool Allowed, string Provenance);
public sealed record AuthorizationCheckResponse(Guid CampaignId, Guid SubjectId, long PolicyRevision, IReadOnlyList<AuthorizationDecision> Decisions);
public sealed record PolicySnapshotResponse(Guid CampaignId, long PolicyRevision, string Status, IReadOnlyList<MembershipPolicyEntry> Members, string Hash, string Signature);
public sealed record MembershipPolicyEntry(Guid SubjectId, string Role, string Status, IReadOnlyList<string> Capabilities);
