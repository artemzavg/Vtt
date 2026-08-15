using Vtt.EngineeringFixture.Application;

namespace Vtt.EngineeringFixture.Infrastructure;

public sealed class ProbeSnapshot
{
    public Guid Id { get; set; }

    public int Value { get; set; }

    public long Version { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class ProbeProjection
{
    public Guid Id { get; set; }

    public int Value { get; set; }

    public long ProjectionVersion { get; set; }

    public long SourceAggregateVersion { get; set; }

    public DateTimeOffset AsOf { get; set; }

    public string Checksum { get; set; } = string.Empty;
}

public sealed class PlatformOperationRecord
{
    public Guid Id { get; set; }

    public string Type { get; set; } = string.Empty;

    public PlatformOperationStatus Status { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public string? ResultChecksum { get; set; }

    public string? ErrorCode { get; set; }

    public bool CancellationRequested { get; set; }
}
