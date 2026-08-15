namespace Vtt.EngineeringFixture.Contracts;

public sealed record IncrementProbeRequest(int Amount);

public sealed record IncrementProbeResponse(
    Guid ProbeId,
    int Value,
    long AggregateVersion,
    Guid EventId,
    bool ProjectionPending,
    bool IdempotencyReplayed,
    string CorrelationId);

public sealed record ProbeProjectionResponse(
    Guid ProbeId,
    int Value,
    long ProjectionVersion,
    long SourceAggregateVersion,
    DateTimeOffset AsOf,
    string Checksum);

public sealed record OperationResponse(
    Guid OperationId,
    string Type,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    string? ResultChecksum,
    string? ErrorCode,
    bool CancellationRequested);

public sealed record QuarantineResponse(
    Guid QuarantineId,
    Guid? EventId,
    string Consumer,
    string Subject,
    string ReasonCode,
    int RetryCount,
    DateTimeOffset QuarantinedAt,
    DateTimeOffset? RetriedAt);

public static class PlatformProblemCodes
{
    public const string AggregateConflict = "aggregate_conflict";
    public const string CommandDeadlineExceeded = "command_deadline_exceeded";
    public const string IdempotencyPayloadMismatch = "idempotency_payload_mismatch";
    public const string InvalidRequest = "invalid_request";
    public const string MalformedRequest = "malformed_request";
    public const string OperationNotFound = "operation_not_found";
    public const string ProjectionNotFound = "projection_not_found";
    public const string UnexpectedError = "unexpected_error";
}
