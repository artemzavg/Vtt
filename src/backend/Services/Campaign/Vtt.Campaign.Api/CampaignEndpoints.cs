using Vtt.Campaign.Contracts;
using App = Vtt.Campaign.Application;
using CampaignAccessService = Vtt.Campaign.Application.CampaignAccessService;

namespace Vtt.Campaign.Api;

internal static class CampaignEndpoints
{
    private const string UserHeader = "X-Vtt-User-Id";
    private const string InternalKeyHeader = "X-Vtt-Internal-Key";

    public static IEndpointRouteBuilder MapCampaignEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api/v1").AddEndpointFilter<TrustedCallerFilter>();
        api.MapPost("/campaigns", CreateAsync);
        api.MapGet("/campaigns", ListAsync);
        api.MapGet("/campaigns/{campaignId:guid}", GetAsync);
        api.MapPatch("/campaigns/{campaignId:guid}", UpdateAsync);
        api.MapPut("/campaigns/{campaignId:guid}/settings", UpdateSettingsAsync);
        api.MapPost("/campaigns/{campaignId:guid}:activate", ActivateAsync);
        api.MapPost("/campaigns/{campaignId:guid}:archive", ArchiveAsync);
        api.MapPost("/campaigns/{campaignId:guid}:restore", RestoreAsync);
        api.MapGet("/campaigns/{campaignId:guid}/members", ListMembersAsync);
        api.MapPatch("/campaigns/{campaignId:guid}/members/{membershipId:guid}", ChangeMemberAsync);
        api.MapDelete("/campaigns/{campaignId:guid}/members/{membershipId:guid}", RemoveMemberAsync);
        api.MapPost("/campaigns/{campaignId:guid}/invitations", CreateInvitationAsync);
        api.MapGet("/campaigns/{campaignId:guid}/invitations", ListInvitationsAsync);
        api.MapDelete("/campaigns/{campaignId:guid}/invitations/{invitationId:guid}", RevokeInvitationAsync);
        api.MapPost("/invitations:accept", AcceptInvitationAsync);
        api.MapPost("/campaigns/{campaignId:guid}/ownership-transfer", RequestTransferAsync);
        api.MapPost("/campaigns/{campaignId:guid}/ownership-transfer:accept", AcceptTransferAsync);
        api.MapGet("/campaigns/{campaignId:guid}/permissions/effective", EffectiveAsync);

        var internalApi = endpoints.MapGroup("/internal/v1").AddEndpointFilter<TrustedCallerFilter>();
        internalApi.MapPost("/authorization/check", CheckAsync);
        internalApi.MapGet("/campaigns/{campaignId:guid}/policy-snapshot", SnapshotAsync);
        return endpoints;
    }

    private static async Task<CampaignResponse> CreateAsync(CreateCampaignRequest request, HttpContext context, CampaignAccessService service, CancellationToken ct) =>
        ToContract(await service.CreateAsync(User(context), new App.CreateCampaignRequest(request.Name, request.Locale, request.TimeZone, request.RulesetVersionId), ct));

    private static async Task<IReadOnlyList<CampaignResponse>> ListAsync(HttpContext context, CampaignAccessService service, CancellationToken ct) =>
        (await service.ListAsync(User(context), ct)).Select(ToContract).ToArray();

    private static async Task<CampaignResponse> GetAsync(Guid campaignId, HttpContext context, CampaignAccessService service, CancellationToken ct) =>
        ToContract(await service.GetAsync(User(context), campaignId, ct));

    private static async Task<CampaignResponse> UpdateAsync(Guid campaignId, UpdateCampaignRequest request, HttpContext context, CampaignAccessService service, CancellationToken ct) =>
        ToContract(await service.UpdateAsync(User(context), campaignId, new App.UpdateCampaignRequest(request.Name, request.Description, request.Locale, request.TimeZone), Version(context), ct));

    private static async Task<CampaignResponse> UpdateSettingsAsync(Guid campaignId, UpdateCampaignSettingsRequest request, HttpContext context, CampaignAccessService service, CancellationToken ct) =>
        ToContract(await service.UpdateSettingsAsync(User(context), campaignId, new App.UpdateCampaignSettingsRequest(request.AutomationLevel, request.DicePolicy), Version(context), ct));

    private static async Task<CampaignResponse> ActivateAsync(Guid campaignId, HttpContext context, CampaignAccessService service, CancellationToken ct) =>
        ToContract(await service.ActivateAsync(User(context), campaignId, Version(context), ct));

    private static async Task<CampaignResponse> ArchiveAsync(Guid campaignId, HttpContext context, CampaignAccessService service, CancellationToken ct) =>
        ToContract(await service.ArchiveAsync(User(context), campaignId, Version(context), ct));

    private static async Task<CampaignResponse> RestoreAsync(Guid campaignId, HttpContext context, CampaignAccessService service, CancellationToken ct) =>
        ToContract(await service.RestoreAsync(User(context), campaignId, Version(context), ct));

    private static async Task<IReadOnlyList<MembershipResponse>> ListMembersAsync(Guid campaignId, HttpContext context, CampaignAccessService service, CancellationToken ct) =>
        (await service.ListMembersAsync(User(context), campaignId, ct)).Select(ToContract).ToArray();

    private static async Task<MembershipResponse> ChangeMemberAsync(Guid campaignId, Guid membershipId, ChangeMembershipRequest request, HttpContext context, CampaignAccessService service, CancellationToken ct) =>
        ToContract(await service.ChangeMemberAsync(User(context), campaignId, membershipId, new App.ChangeMembershipRequest(request.Role, request.Status), Version(context), ct));

    private static async Task<IResult> RemoveMemberAsync(Guid campaignId, Guid membershipId, HttpContext context, CampaignAccessService service, CancellationToken ct)
    {
        await service.RemoveMemberAsync(User(context), campaignId, membershipId, Version(context), ct);
        return Results.NoContent();
    }

    private static async Task<InvitationCreatedResponse> CreateInvitationAsync(Guid campaignId, CreateInvitationRequest request, HttpContext context, CampaignAccessService service, CancellationToken ct) =>
        ToContract(await service.CreateInvitationAsync(User(context), campaignId, new App.CreateInvitationRequest(request.Role, request.ExpiresAt, request.MaxUses), ct));

    private static async Task<IReadOnlyList<InvitationResponse>> ListInvitationsAsync(Guid campaignId, HttpContext context, CampaignAccessService service, CancellationToken ct) =>
        (await service.ListInvitationsAsync(User(context), campaignId, ct)).Select(ToContract).ToArray();

    private static async Task<IResult> RevokeInvitationAsync(Guid campaignId, Guid invitationId, HttpContext context, CampaignAccessService service, CancellationToken ct)
    {
        await service.RevokeInvitationAsync(User(context), campaignId, invitationId, Version(context), ct);
        return Results.NoContent();
    }

    private static async Task<CampaignResponse> AcceptInvitationAsync(AcceptInvitationRequest request, HttpContext context, CampaignAccessService service, CancellationToken ct) =>
        ToContract(await service.AcceptInvitationAsync(User(context), request.Token, ct));

    private static async Task<IResult> RequestTransferAsync(Guid campaignId, RequestOwnershipTransferRequest request, HttpContext context, CampaignAccessService service, CancellationToken ct)
    {
        var transferId = await service.RequestOwnershipTransferAsync(User(context), campaignId, request.TargetUserId, ct);
        return Results.Accepted(value: new { transferId });
    }

    private static async Task<CampaignResponse> AcceptTransferAsync(Guid campaignId, HttpContext context, CampaignAccessService service, CancellationToken ct) =>
        ToContract(await service.AcceptOwnershipTransferAsync(User(context), campaignId, ct));

    private static async Task<AuthorizationCheckResponse> EffectiveAsync(Guid campaignId, HttpContext context, CampaignAccessService service, CancellationToken ct) =>
        ToContract(await service.CheckAsync(campaignId, User(context), CampaignCapabilitiesForExplanation, ct));

    private static async Task<AuthorizationCheckResponse> CheckAsync(AuthorizationCheckRequest request, CampaignAccessService service, CancellationToken ct) =>
        ToContract(await service.CheckAsync(request.CampaignId, request.SubjectId, request.Actions, ct));

    private static async Task<PolicySnapshotResponse> SnapshotAsync(Guid campaignId, CampaignAccessService service, CancellationToken ct) =>
        ToContract(await service.SnapshotAsync(campaignId, ct));

    private static CampaignResponse ToContract(App.CampaignResponse value) => new(
        value.CampaignId, value.OwnerId, value.Name, value.Description, value.Locale, value.TimeZone,
        value.RulesetVersionId, value.Status, value.AutomationLevel, value.DicePolicy, value.Version,
        value.PolicyRevision, value.Role, value.EffectiveCapabilities);

    private static MembershipResponse ToContract(App.MembershipResponse value) => new(
        value.MembershipId, value.UserId, value.Role, value.Status, value.Version, value.JoinedAt);

    private static InvitationCreatedResponse ToContract(App.InvitationCreatedResponse value) => new(
        value.InvitationId, value.Token, value.Role, value.ExpiresAt, value.MaxUses, value.Version);

    private static InvitationResponse ToContract(App.InvitationResponse value) => new(
        value.InvitationId, value.Role, value.ExpiresAt, value.MaxUses, value.UseCount, value.Status, value.Version);

    private static AuthorizationCheckResponse ToContract(App.AuthorizationCheckResponse value) => new(
        value.CampaignId, value.SubjectId, value.PolicyRevision,
        value.Decisions.Select(decision => new AuthorizationDecision(decision.Action, decision.Allowed, decision.Provenance)).ToArray());

    private static PolicySnapshotResponse ToContract(App.PolicySnapshotResponse value) => new(
        value.CampaignId, value.PolicyRevision, value.Status,
        value.Members.Select(member => new MembershipPolicyEntry(member.SubjectId, member.Role, member.Status, member.Capabilities)).ToArray(),
        value.Hash, value.Signature);

    private static readonly string[] CampaignCapabilitiesForExplanation =
    [
        "campaign.read", "campaign.update", "campaign.activate", "campaign.archive",
        "campaign.restore", "members.read", "members.manage", "invitations.manage",
        "ownership.transfer", "gameplay.write",
    ];

    private static Guid User(HttpContext context) => Guid.Parse(context.Request.Headers[UserHeader].ToString());

    private static long Version(HttpContext context)
    {
        var value = context.Request.Headers.IfMatch.ToString().Trim('"');
        return long.TryParse(value, out var version) ? version : throw new BadHttpRequestException("If-Match is required.");
    }

    private sealed class TrustedCallerFilter(IConfiguration configuration) : IEndpointFilter
    {
        public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
        {
            var expected = configuration["Campaign:InternalApiKey"] ?? "local-campaign-internal-key-change-me";
            var supplied = context.HttpContext.Request.Headers[InternalKeyHeader].ToString();
            var user = context.HttpContext.Request.Headers[UserHeader].ToString();
            if (!CryptographicEquals(expected, supplied) || !Guid.TryParse(user, out _))
            {
                return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "campaign.unauthorized",
                    extensions: new Dictionary<string, object?> { ["code"] = "campaign.unauthorized" });
            }

            return await next(context);
        }

        private static bool CryptographicEquals(string expected, string supplied) =>
            expected.Length == supplied.Length && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(expected), System.Text.Encoding.UTF8.GetBytes(supplied));
    }
}
