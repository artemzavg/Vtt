namespace Vtt.Persistence.Marten;

public enum OutboxStatus
{
    Pending,
    Published,
    DeadLetter,
}

public sealed class OutboxMessage
{
    public Guid Id { get; set; }

    public Guid EventId { get; set; }

    public string Subject { get; set; } = string.Empty;

    public string Payload { get; set; } = string.Empty;

    public string? TraceParent { get; set; }

    public OutboxStatus Status { get; set; }

    public int AttemptCount { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public DateTimeOffset NextAttemptAt { get; set; }

    public DateTimeOffset? PublishedAt { get; set; }

    public string? LastErrorCode { get; set; }

    public static OutboxMessage Pending(
        Guid eventId,
        string subject,
        string payload,
        string? traceParent,
        DateTimeOffset occurredAt) => new()
        {
            Id = eventId,
            EventId = eventId,
            Subject = subject,
            Payload = payload,
            TraceParent = traceParent,
            Status = OutboxStatus.Pending,
            OccurredAt = occurredAt,
            NextAttemptAt = occurredAt,
        };
}

public enum InboxOutcome
{
    Applied,
    Duplicate,
    IgnoredOld,
    ResyncedGap,
    Quarantined,
}

public sealed class InboxRecord
{
    public string Id { get; set; } = string.Empty;

    public string Consumer { get; set; } = string.Empty;

    public Guid EventId { get; set; }

    public Guid AggregateId { get; set; }

    public long AggregateVersion { get; set; }

    public InboxOutcome Outcome { get; set; }

    public DateTimeOffset ProcessedAt { get; set; }
}

public sealed class IdempotencyRecord
{
    public string Id { get; set; } = string.Empty;

    public string PrincipalHash { get; set; } = string.Empty;

    public string Route { get; set; } = string.Empty;

    public string KeyHash { get; set; } = string.Empty;

    public string RequestHash { get; set; } = string.Empty;

    public Guid CommandId { get; set; }

    public int ResponseStatusCode { get; set; }

    public string ResponseBody { get; set; } = string.Empty;

    public long AggregateVersion { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }
}

public sealed class QuarantinedMessage
{
    public Guid Id { get; set; }

    public Guid? EventId { get; set; }

    public string Consumer { get; set; } = string.Empty;

    public string Subject { get; set; } = string.Empty;

    public string ReasonCode { get; set; } = string.Empty;

    public string Payload { get; set; } = string.Empty;

    public int RetryCount { get; set; }

    public DateTimeOffset QuarantinedAt { get; set; }

    public DateTimeOffset? RetriedAt { get; set; }
}

public sealed class ProjectionCheckpoint
{
    public string Id { get; set; } = string.Empty;

    public long LastSequence { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
