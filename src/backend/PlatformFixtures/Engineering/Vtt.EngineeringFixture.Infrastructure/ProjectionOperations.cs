using System.Diagnostics;
using System.Diagnostics.Metrics;
using Marten;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vtt.EngineeringFixture.Application;
using Vtt.EngineeringFixture.Domain;

namespace Vtt.EngineeringFixture.Infrastructure;

public sealed class MartenProjectionOperations(
    IDocumentStore documentStore,
    TimeProvider timeProvider) : IProjectionOperations
{
    public async Task<PlatformOperationResult> StartRebuildAsync(
        CancellationToken cancellationToken)
    {
        var operation = new PlatformOperationRecord
        {
            Id = Guid.CreateVersion7(),
            Type = "probe-projection-rebuild-v1",
            Status = PlatformOperationStatus.Pending,
            CreatedAt = timeProvider.GetUtcNow(),
        };
        await using var session = documentStore.LightweightSession();
        session.Insert(operation);
        await session.SaveChangesAsync(cancellationToken);
        return Map(operation);
    }

    public async Task<PlatformOperationResult?> FindAsync(
        Guid operationId,
        CancellationToken cancellationToken)
    {
        await using var session = documentStore.QuerySession();
        var operation = await session.LoadAsync<PlatformOperationRecord>(
            operationId,
            cancellationToken);
        return operation is null ? null : Map(operation);
    }

    public async Task<bool> RequestCancellationAsync(
        Guid operationId,
        CancellationToken cancellationToken)
    {
        await using var session = documentStore.LightweightSession();
        var operation = await session.LoadAsync<PlatformOperationRecord>(
            operationId,
            cancellationToken);
        if (operation is null || operation.Status is
            PlatformOperationStatus.Completed or
            PlatformOperationStatus.Cancelled or
            PlatformOperationStatus.Failed)
        {
            return false;
        }

        operation.CancellationRequested = true;
        if (operation.Status == PlatformOperationStatus.Pending)
        {
            operation.Status = PlatformOperationStatus.Cancelled;
            operation.CompletedAt = timeProvider.GetUtcNow();
        }

        session.Store(operation);
        await session.SaveChangesAsync(cancellationToken);
        return true;
    }

    internal static PlatformOperationResult Map(PlatformOperationRecord operation) => new(
        operation.Id,
        operation.Type,
        operation.Status,
        operation.CreatedAt,
        operation.CompletedAt,
        operation.ResultChecksum,
        operation.ErrorCode,
        operation.CancellationRequested);
}

public sealed class ProjectionRebuildWorker(
    IDocumentStore documentStore,
    TimeProvider timeProvider,
    ILogger<ProjectionRebuildWorker> logger) : BackgroundService
{
    private static readonly ActivitySource ActivitySource = new("Vtt.Projections");
    private static readonly Meter Meter = new("Vtt.Projections");
    private static readonly Histogram<double> RebuildDuration = Meter.CreateHistogram<double>(
        "vtt.projection.rebuild.duration",
        "ms");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var operationId = await FindPendingOperationAsync(stoppingToken);
                if (operationId is null)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(500), stoppingToken);
                    continue;
                }

                await RebuildAsync(operationId.Value, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                EngineeringLog.ProjectionWorkerIterationFailed(
                    logger,
                    exception.GetType().Name,
                    exception);
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
        }
    }

    private async Task<Guid?> FindPendingOperationAsync(CancellationToken cancellationToken)
    {
        await using var session = documentStore.QuerySession();
        var operation = await session.Query<PlatformOperationRecord>()
            .Where(operation => operation.Status == PlatformOperationStatus.Pending)
            .OrderBy(operation => operation.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        return operation?.Id;
    }

    private async Task RebuildAsync(Guid operationId, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        using var activity = ActivitySource.StartActivity("projection rebuild");
        activity?.SetTag("vtt.operation.id", operationId.ToString("D"));

        try
        {
            await using var session = documentStore.LightweightSession();
            var operation = await session.LoadAsync<PlatformOperationRecord>(
                operationId,
                cancellationToken);
            if (operation is null)
            {
                return;
            }
            if (operation.CancellationRequested)
            {
                operation.Status = PlatformOperationStatus.Cancelled;
                operation.CompletedAt = timeProvider.GetUtcNow();
                session.Store(operation);
                await session.SaveChangesAsync(cancellationToken);
                return;
            }

            operation.Status = PlatformOperationStatus.Running;
            operation.StartedAt = timeProvider.GetUtcNow();
            session.Store(operation);
            await session.SaveChangesAsync(cancellationToken);

            await using var rebuild = documentStore.LightweightSession();
            var snapshots = await rebuild.Query<ProbeSnapshot>()
                .OrderBy(snapshot => snapshot.Id)
                .ToListAsync(cancellationToken);
            rebuild.DeleteWhere<ProbeProjection>(_ => true);
            var checksums = new List<string>(snapshots.Count);

            foreach (var snapshot in snapshots)
            {
                var aggregate = await ProbeEventStream.LoadAsync(
                    rebuild,
                    snapshot.Id,
                    cancellationToken);
                if (aggregate.Version == 0)
                {
                    throw new InvalidOperationException("Projection replay missed a stream.");
                }
                var checksum = StableHash.Projection(
                    aggregate.Id,
                    aggregate.Value,
                    aggregate.Version);
                rebuild.Store(new ProbeProjection
                {
                    Id = aggregate.Id,
                    Value = aggregate.Value,
                    ProjectionVersion = aggregate.Version,
                    SourceAggregateVersion = aggregate.Version,
                    AsOf = timeProvider.GetUtcNow(),
                    Checksum = checksum,
                });
                checksums.Add(checksum);
            }

            var resultChecksum = StableHash.Sha256(string.Join('|', checksums));
            var completed = await rebuild.LoadAsync<PlatformOperationRecord>(
                operationId,
                cancellationToken)
                ?? throw new InvalidOperationException("Projection operation disappeared.");
            completed.Status = completed.CancellationRequested
                ? PlatformOperationStatus.Cancelled
                : PlatformOperationStatus.Completed;
            completed.CompletedAt = timeProvider.GetUtcNow();
            completed.ResultChecksum = completed.Status == PlatformOperationStatus.Completed
                ? resultChecksum
                : null;
            rebuild.Store(completed);
            await rebuild.SaveChangesAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            await MarkFailedAsync(operationId, exception.GetType().Name, cancellationToken);
            EngineeringLog.ProjectionRebuildFailed(
                logger,
                operationId,
                exception.GetType().Name,
                exception);
        }
        finally
        {
            RebuildDuration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
    }

    private async Task MarkFailedAsync(
        Guid operationId,
        string errorCode,
        CancellationToken cancellationToken)
    {
        await using var session = documentStore.LightweightSession();
        var operation = await session.LoadAsync<PlatformOperationRecord>(
            operationId,
            cancellationToken);
        if (operation is null)
        {
            return;
        }

        operation.Status = PlatformOperationStatus.Failed;
        operation.ErrorCode = errorCode;
        operation.CompletedAt = timeProvider.GetUtcNow();
        session.Store(operation);
        await session.SaveChangesAsync(cancellationToken);
    }
}
