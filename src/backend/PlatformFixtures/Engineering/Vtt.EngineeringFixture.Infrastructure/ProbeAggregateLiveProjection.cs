using JasperFx.Events;
using Marten.Events.Aggregation;
using Vtt.EngineeringFixture.Domain;

namespace Vtt.EngineeringFixture.Infrastructure;

internal sealed class ProbeAggregateLiveProjection
    : SingleStreamProjection<ProbeAggregate, Guid>
{
    public override ProbeAggregate Evolve(
        ProbeAggregate? snapshot,
        Guid id,
        IEvent @event)
    {
        snapshot ??= new ProbeAggregate();

        if (@event.Data is ProbeIncremented incremented)
        {
            snapshot.Fold(incremented);
        }

        return snapshot;
    }
}
