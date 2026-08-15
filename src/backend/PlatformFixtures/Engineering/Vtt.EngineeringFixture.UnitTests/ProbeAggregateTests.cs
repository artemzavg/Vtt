using FsCheck;
using FsCheck.Xunit;
using Vtt.EngineeringFixture.Domain;

namespace Vtt.EngineeringFixture.UnitTests;

public sealed class ProbeAggregateTests
{
    [Fact]
    public void GivenEmptyAggregateWhenIncrementedThenEmitsVersionOneEvent()
    {
        var id = Guid.CreateVersion7();
        var occurredAt = new DateTimeOffset(2026, 8, 15, 10, 0, 0, TimeSpan.Zero);

        var decision = new ProbeAggregate().DecideIncrement(id, 7, occurredAt);

        var @event = Assert.IsType<ProbeIncremented>(Assert.Single(decision.Events));
        Assert.Equal(id, @event.ProbeId);
        Assert.Equal(7, @event.ResultingValue);
        Assert.Equal(1, @event.AggregateVersion);
        Assert.Equal(occurredAt, @event.OccurredAt);
    }

    [Fact]
    public void GivenHistoryWhenRehydratedThenNextEventUsesCurrentValueAndVersion()
    {
        var id = Guid.CreateVersion7();
        var aggregate = new ProbeAggregate();
        aggregate.Fold(new ProbeIncremented(id, 2, 2, 1, DateTimeOffset.UnixEpoch));
        aggregate.Fold(new ProbeIncremented(id, 3, 5, 2, DateTimeOffset.UnixEpoch));

        var decision = aggregate.DecideIncrement(id, 4, DateTimeOffset.UnixEpoch);

        var @event = Assert.IsType<ProbeIncremented>(Assert.Single(decision.Events));
        Assert.Equal(9, @event.ResultingValue);
        Assert.Equal(3, @event.AggregateVersion);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1001)]
    public void InvalidIncrementIsRejectedWithoutEvents(int amount)
    {
        var aggregate = new ProbeAggregate();

        var exception = Assert.Throws<ProbeInvariantException>(() =>
            aggregate.DecideIncrement(Guid.CreateVersion7(), amount, DateTimeOffset.UnixEpoch));

        Assert.Equal("increment_out_of_range", exception.Code);
        Assert.Equal(0, aggregate.Version);
    }

    [Property(MaxTest = 200)]
    public bool ValidIncrementsAreAdditive(PositiveInt first, PositiveInt second)
    {
        var id = Guid.CreateVersion7();
        var firstAmount = first.Get % 1000 + 1;
        var secondAmount = second.Get % 1000 + 1;
        var aggregate = new ProbeAggregate();
        var firstEvent = (ProbeIncremented)Assert.Single(
            aggregate.DecideIncrement(id, firstAmount, DateTimeOffset.UnixEpoch).Events);
        aggregate.Fold(firstEvent);
        var secondEvent = (ProbeIncremented)Assert.Single(
            aggregate.DecideIncrement(id, secondAmount, DateTimeOffset.UnixEpoch).Events);

        return secondEvent.ResultingValue == firstAmount + secondAmount &&
               secondEvent.AggregateVersion == 2;
    }
}
