using Marten;
using Vtt.EngineeringFixture.Application;

namespace Vtt.EngineeringFixture.Infrastructure;

public sealed class MartenProbeProjectionReader(IDocumentStore documentStore)
    : IProbeProjectionReader
{
    public async Task<ProbeProjectionResult?> FindAsync(
        Guid probeId,
        CancellationToken cancellationToken)
    {
        await using var session = documentStore.QuerySession();
        var projection = await session.LoadAsync<ProbeProjection>(
            probeId,
            cancellationToken);
        return projection is null
            ? null
            : new ProbeProjectionResult(
                projection.Id,
                projection.Value,
                projection.ProjectionVersion,
                projection.SourceAggregateVersion,
                projection.AsOf,
                projection.Checksum);
    }
}
