using Vtt.Campaign.Domain;

namespace Vtt.Campaign.UnitTests;

public sealed class CampaignDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 21, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(CampaignRole.Owner, CampaignCapabilities.OwnershipTransfer, true)]
    [InlineData(CampaignRole.CoGm, CampaignCapabilities.OwnershipTransfer, false)]
    [InlineData(CampaignRole.CoGm, CampaignCapabilities.InvitationsManage, true)]
    [InlineData(CampaignRole.Player, CampaignCapabilities.GameplayWrite, true)]
    [InlineData(CampaignRole.Player, CampaignCapabilities.Update, false)]
    [InlineData(CampaignRole.Observer, CampaignCapabilities.MembersRead, true)]
    [InlineData(CampaignRole.Observer, CampaignCapabilities.GameplayWrite, false)]
    [InlineData(CampaignRole.Owner, "unknown.action", false)]
    public void CapabilityMatrixIsDenyByDefault(CampaignRole role, string capability, bool expected) =>
        Assert.Equal(expected, CampaignCapabilities.Allows(role, capability));

    [Fact]
    public void CampaignCannotActivateWithoutPublishedRulesetFixture()
    {
        var campaign = CampaignAggregate.Create(Guid.NewGuid(), "Arrakis", "ru-RU", "Europe/Moscow", "draft@1", Now);

        var error = Assert.Throws<CampaignDomainException>(() => campaign.Activate(1, Now));

        Assert.Equal("campaign.ruleset_not_published", error.Code);
        Assert.Equal(CampaignStatus.Draft, campaign.Status);
        Assert.Equal(1, campaign.Version);
    }

    [Fact]
    public void ArchivedCampaignCanBeRestoredToDraftWithNewPolicyRevision()
    {
        var campaign = CampaignAggregate.Create(Guid.NewGuid(), "Arrakis", "ru-RU", "Europe/Moscow", CampaignAggregate.PublishedRulesetFixture, Now);
        campaign.Activate(1, Now);
        campaign.Archive(2, Now);

        campaign.Restore(3, Now);

        Assert.Equal(CampaignStatus.Draft, campaign.Status);
        Assert.Equal(4, campaign.Version);
        Assert.Equal(4, campaign.PolicyRevision);
    }

    [Fact]
    public void LastOwnerCannotBeSuspendedOrDemoted()
    {
        var owner = Membership.CreateOwner(Guid.NewGuid(), Guid.NewGuid(), Now);

        var error = Assert.Throws<CampaignDomainException>(() =>
            owner.Change(CampaignRole.CoGm, MembershipStatus.Active, 1, Now));

        Assert.Equal("campaign.last_owner_required", error.Code);
        Assert.Equal(CampaignRole.Owner, owner.Role);
    }

    [Fact]
    public void InvitationNeverAcceptsOwnerAndEnforcesExpiryAndMaxUse()
    {
        var campaignId = Guid.NewGuid();
        Assert.Throws<CampaignDomainException>(() =>
            Invitation.Create(campaignId, Guid.NewGuid(), CampaignRole.Owner, new byte[32], Now.AddHours(1), 1, Now));
        var invitation = Invitation.Create(campaignId, Guid.NewGuid(), CampaignRole.Player, new byte[32], Now.AddHours(1), 1, Now);

        invitation.Consume(Now);

        Assert.False(invitation.IsUsable(Now));
        Assert.Throws<CampaignDomainException>(() => invitation.Consume(Now));
    }

    [Fact]
    public void StaleAggregateVersionIsRejectedWithoutMutation()
    {
        var campaign = CampaignAggregate.Create(Guid.NewGuid(), "Arrakis", "ru-RU", "Europe/Moscow", null, Now);

        Assert.Throws<CampaignConcurrencyException>(() =>
            campaign.UpdateMetadata("Caladan", null, "ru-RU", "Europe/Moscow", 0, Now));

        Assert.Equal("Arrakis", campaign.Name);
        Assert.Equal(1, campaign.Version);
    }
}
