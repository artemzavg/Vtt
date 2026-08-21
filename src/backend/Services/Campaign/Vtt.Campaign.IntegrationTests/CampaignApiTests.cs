using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using Testcontainers.PostgreSql;
using Vtt.Campaign.Api;
using Vtt.Campaign.Contracts;
using Vtt.Campaign.Infrastructure;

namespace Vtt.Campaign.IntegrationTests;

public sealed class CampaignApiFixture : IAsyncLifetime
{
    private const int NatsPort = 4222;
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17.6-alpine")
        .WithDatabase("vtt_campaign")
        .WithUsername("vtt_campaign")
        .WithPassword("campaign-test-password")
        .Build();
    private readonly IContainer _nats = new ContainerBuilder("nats:2.11.3-alpine")
        .WithCommand("--jetstream", "--store_dir", "/data/jetstream")
        .WithPortBinding(NatsPort, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(NatsPort))
        .Build();

    public WebApplicationFactory<ApiAssemblyMarker> Factory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await _nats.StartAsync();
        await EnsureStreamAsync();
        Factory = new WebApplicationFactory<ApiAssemblyMarker>().WithWebHostBuilder(builder => builder
            .UseSetting("ConnectionStrings:campaign", _postgres.GetConnectionString())
            .UseSetting("VTT_APPLY_SCHEMA", "true")
            .UseSetting("NATS_URL", NatsUrl)
            .UseSetting("Campaign:InternalApiKey", CampaignApiTests.InternalKey)
            .UseSetting("Campaign:PolicySigningKey", "integration-test-policy-signing-key-32-bytes"));
    }

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
        await Task.WhenAll(_postgres.DisposeAsync().AsTask(), _nats.DisposeAsync().AsTask());
    }

    public NatsConnection CreateNatsConnection() => new(new NatsOpts { Url = NatsUrl });

    private string NatsUrl => $"nats://{_nats.Hostname}:{_nats.GetMappedPublicPort(NatsPort)}";

    private async Task EnsureStreamAsync()
    {
        await using var connection = CreateNatsConnection();
        var context = new NatsJSContext(connection);
        await context.CreateOrUpdateStreamAsync(new StreamConfig("VTT_EVENTS", ["vtt.>"]));
    }
}

public sealed class CampaignApiTests(CampaignApiFixture fixture) : IClassFixture<CampaignApiFixture>
{
    internal const string InternalKey = "integration-campaign-internal-key";

    [Fact]
    public async Task InviteIsConsumedOnceAndCrossTenantReadsAreHidden()
    {
        var ownerId = Guid.NewGuid();
        var playerId = Guid.NewGuid();
        var strangerId = Guid.NewGuid();
        using var owner = Client(ownerId);
        var campaign = await CreateCampaignAsync(owner, "Arrakis");
        var invitation = await CreateInvitationAsync(owner, campaign.CampaignId, maxUses: 1);
        using var player = Client(playerId);
        using var stranger = Client(strangerId);

        using var accept = await player.PostAsJsonAsync("/api/v1/invitations:accept", new AcceptInvitationRequest(invitation.Token));
        using var replay = await stranger.PostAsJsonAsync("/api/v1/invitations:accept", new AcceptInvitationRequest(invitation.Token));
        using var hidden = await stranger.GetAsync($"/api/v1/campaigns/{campaign.CampaignId:D}");

        Assert.Equal(HttpStatusCode.OK, accept.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        Assert.DoesNotContain(invitation.Token, await (await owner.GetAsync($"/api/v1/campaigns/{campaign.CampaignId:D}/invitations")).Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConcurrentInviteUseHonorsMaxUsesAtomically()
    {
        using var owner = Client(Guid.NewGuid());
        var campaign = await CreateCampaignAsync(owner, "Caladan");
        var invitation = await CreateInvitationAsync(owner, campaign.CampaignId, maxUses: 1);
        using var first = Client(Guid.NewGuid());
        using var second = Client(Guid.NewGuid());

        var responses = await Task.WhenAll(
            first.PostAsJsonAsync("/api/v1/invitations:accept", new AcceptInvitationRequest(invitation.Token)),
            second.PostAsJsonAsync("/api/v1/invitations:accept", new AcceptInvitationRequest(invitation.Token)));

        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, response => response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Conflict);
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task RoleChangeIsVersionedAndLastOwnerInvariantIsEnforced()
    {
        var ownerId = Guid.NewGuid();
        var playerId = Guid.NewGuid();
        using var owner = Client(ownerId);
        var campaign = await CreateCampaignAsync(owner, "Giedi Prime");
        var invitation = await CreateInvitationAsync(owner, campaign.CampaignId, 1);
        using var player = Client(playerId);
        (await player.PostAsJsonAsync("/api/v1/invitations:accept", new AcceptInvitationRequest(invitation.Token))).EnsureSuccessStatusCode();
        var members = await owner.GetFromJsonAsync<IReadOnlyList<MembershipResponse>>($"/api/v1/campaigns/{campaign.CampaignId:D}/members");
        var playerMembership = Assert.Single(members!, member => member.UserId == playerId);
        var ownerMembership = Assert.Single(members!, member => member.UserId == ownerId);

        using var first = Request(HttpMethod.Patch, $"/api/v1/campaigns/{campaign.CampaignId:D}/members/{playerMembership.MembershipId:D}",
            new ChangeMembershipRequest("CoGm", "Active"), playerMembership.Version);
        using var stale = Request(HttpMethod.Patch, first.RequestUri!.OriginalString,
            new ChangeMembershipRequest("Observer", "Active"), playerMembership.Version);
        using var changed = await owner.SendAsync(first);
        using var conflict = await owner.SendAsync(stale);
        using var demoteOwnerRequest = Request(HttpMethod.Patch, $"/api/v1/campaigns/{campaign.CampaignId:D}/members/{ownerMembership.MembershipId:D}",
            new ChangeMembershipRequest("CoGm", "Active"), ownerMembership.Version);
        using var demoteOwner = await owner.SendAsync(demoteOwnerRequest);

        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, demoteOwner.StatusCode);
    }

    [Fact]
    public async Task OwnershipChangesOnlyAfterTargetAcceptsAndSnapshotIsSigned()
    {
        var ownerId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        using var owner = Client(ownerId);
        var campaign = await CreateCampaignAsync(owner, "Ix");
        var invitation = await CreateInvitationAsync(owner, campaign.CampaignId, 1);
        using var target = Client(targetId);
        (await target.PostAsJsonAsync("/api/v1/invitations:accept", new AcceptInvitationRequest(invitation.Token))).EnsureSuccessStatusCode();

        using var requested = await owner.PostAsJsonAsync($"/api/v1/campaigns/{campaign.CampaignId:D}/ownership-transfer", new RequestOwnershipTransferRequest(targetId));
        var before = await owner.GetFromJsonAsync<CampaignResponse>($"/api/v1/campaigns/{campaign.CampaignId:D}");
        await using var nats = fixture.CreateNatsConnection();
        using var receiveTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var receivedPolicyEvent = ReceivePolicyEventAsync(nats, campaign.CampaignId, receiveTimeout.Token);
        using var accepted = await target.PostAsync($"/api/v1/campaigns/{campaign.CampaignId:D}/ownership-transfer:accept", null);
        var after = await target.GetFromJsonAsync<CampaignResponse>($"/api/v1/campaigns/{campaign.CampaignId:D}");
        var snapshot = await target.GetFromJsonAsync<PolicySnapshotResponse>($"/internal/v1/campaigns/{campaign.CampaignId:D}/policy-snapshot");

        Assert.Equal(HttpStatusCode.Accepted, requested.StatusCode);
        Assert.Equal(ownerId, before!.OwnerId);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Equal(targetId, after!.OwnerId);
        Assert.Equal("Owner", after.Role);
        Assert.False(string.IsNullOrWhiteSpace(snapshot!.Hash));
        Assert.False(string.IsNullOrWhiteSpace(snapshot.Signature));
        var envelope = await receivedPolicyEvent;
        Assert.Equal("CampaignPolicyRevisionChanged.v1", envelope.GetProperty("eventType").GetString());
        Assert.Equal(campaign.CampaignId, envelope.GetProperty("aggregate").GetProperty("id").GetGuid());
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<CampaignDbContext>();
        var streamVersions = await database.DomainEvents
            .Where(domainEvent => domainEvent.StreamId == campaign.CampaignId)
            .OrderBy(domainEvent => domainEvent.StreamVersion)
            .Select(domainEvent => domainEvent.StreamVersion)
            .ToArrayAsync();
        Assert.Equal(Enumerable.Range(1, streamVersions.Length).Select(value => (long)value), streamVersions);
        Assert.Contains(await database.IntegrationOutbox.ToListAsync(),
            integrationEvent => integrationEvent.EventType == "CampaignPolicyRevisionChanged.v1" &&
                integrationEvent.PolicyRevision == after.PolicyRevision &&
                integrationEvent.PublishedAt is not null);
    }

    private HttpClient Client(Guid userId)
    {
        var client = fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Vtt-Internal-Key", InternalKey);
        client.DefaultRequestHeaders.Add("X-Vtt-User-Id", userId.ToString("D"));
        return client;
    }

    private static async Task<CampaignResponse> CreateCampaignAsync(HttpClient client, string name)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/campaigns",
            new CreateCampaignRequest(name, "ru-RU", "Europe/Moscow", "dnd5e-srd@1.0.0"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CampaignResponse>())!;
    }

    private static async Task<InvitationCreatedResponse> CreateInvitationAsync(HttpClient client, Guid campaignId, int maxUses)
    {
        using var response = await client.PostAsJsonAsync($"/api/v1/campaigns/{campaignId:D}/invitations",
            new CreateInvitationRequest("Player", DateTimeOffset.UtcNow.AddHours(1), maxUses));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<InvitationCreatedResponse>())!;
    }

    private static HttpRequestMessage Request(HttpMethod method, string uri, object body, long version)
    {
        var request = new HttpRequestMessage(method, uri) { Content = JsonContent.Create(body) };
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
        return request;
    }

    private static async Task<JsonElement> ReceivePolicyEventAsync(
        NatsConnection connection,
        Guid campaignId,
        CancellationToken cancellationToken)
    {
        await foreach (var message in connection.SubscribeAsync<string>(
                           CampaignOutboxRelay.SubjectFor("CampaignPolicyRevisionChanged.v1"),
                           cancellationToken: cancellationToken))
        {
            using var document = JsonDocument.Parse(message.Data ?? "{}");
            var root = document.RootElement;
            if (root.TryGetProperty("aggregate", out var aggregate) &&
                aggregate.TryGetProperty("id", out var id) &&
                id.GetGuid() == campaignId)
            {
                return root.Clone();
            }
        }

        throw new TimeoutException("Campaign policy event was not delivered.");
    }
}
