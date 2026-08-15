using Marten;
using Vtt.EngineeringFixture.Domain;

namespace Vtt.EngineeringFixture.Infrastructure;

internal static class ProbeEventStream
{
    public static async Task<ProbeAggregate> LoadAsync(
        IQuerySession session,
        Guid probeId,
        CancellationToken cancellationToken)
    {
        var aggregate = new ProbeAggregate();
        var events = await session.Events.FetchStreamAsync(
            probeId,
            token: cancellationToken);
        foreach (var @event in events)
        {
            if (@event.Data is ProbeIncremented incremented)
            {
                aggregate.Fold(incremented);
            }
        }

        return aggregate;
    }
}
