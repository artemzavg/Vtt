using Vtt.Cqrs;

namespace Vtt.EngineeringFixture.Application;

public sealed record GetProbeProjectionQuery(Guid ProbeId)
    : IQuery<ProbeProjectionResult?>;

public sealed record ProbeProjectionResult(
    Guid ProbeId,
    int Value,
    long ProjectionVersion,
    long SourceAggregateVersion,
    DateTimeOffset AsOf,
    string Checksum);

public interface IProbeProjectionReader
{
    Task<ProbeProjectionResult?> FindAsync(
        Guid probeId,
        CancellationToken cancellationToken);
}

public sealed class GetProbeProjectionHandler(IProbeProjectionReader reader)
    : IQueryHandler<GetProbeProjectionQuery, ProbeProjectionResult?>
{
    public Task<ProbeProjectionResult?> HandleAsync(
        GetProbeProjectionQuery query,
        CancellationToken cancellationToken) =>
        reader.FindAsync(query.ProbeId, cancellationToken);
}

public enum PlatformOperationStatus
{
    Pending,
    Running,
    Completed,
    Cancelled,
    Failed,
}

public sealed record PlatformOperationResult(
    Guid OperationId,
    string Type,
    PlatformOperationStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    string? ResultChecksum,
    string? ErrorCode,
    bool CancellationRequested);

public interface IProjectionOperations
{
    Task<PlatformOperationResult> StartRebuildAsync(CancellationToken cancellationToken);

    Task<PlatformOperationResult?> FindAsync(
        Guid operationId,
        CancellationToken cancellationToken);

    Task<bool> RequestCancellationAsync(
        Guid operationId,
        CancellationToken cancellationToken);
}

public sealed record QuarantineResult(
    Guid QuarantineId,
    Guid? EventId,
    string Consumer,
    string Subject,
    string ReasonCode,
    int RetryCount,
    DateTimeOffset QuarantinedAt,
    DateTimeOffset? RetriedAt);

public interface IMessageOperations
{
    Task<IReadOnlyList<QuarantineResult>> ListQuarantineAsync(
        CancellationToken cancellationToken);

    Task RetryQuarantinedAsync(Guid quarantineId, CancellationToken cancellationToken);

    Task RetryOutboxAsync(Guid outboxMessageId, CancellationToken cancellationToken);
}
