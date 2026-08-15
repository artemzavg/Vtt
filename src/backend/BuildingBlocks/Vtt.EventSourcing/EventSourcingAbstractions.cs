namespace Vtt.EventSourcing;

public interface IDomainEvent;

public readonly record struct ExpectedVersion
{
    public static ExpectedVersion Initial => new(0);

    public ExpectedVersion(long value)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "Expected version cannot be negative.");
        }

        Value = value;
    }

    public long Value { get; }
}

public sealed record EventDecision(IReadOnlyList<IDomainEvent> Events)
{
    public static EventDecision None { get; } = new([]);

    public static EventDecision Emit(params IDomainEvent[] events)
    {
        ArgumentNullException.ThrowIfNull(events);
        return new EventDecision(events);
    }
}

public sealed class AggregateConflictException(
    Guid aggregateId,
    long expectedVersion,
    long currentVersion)
    : Exception("The aggregate changed after the caller read it.")
{
    public Guid AggregateId { get; } = aggregateId;

    public long ExpectedVersion { get; } = expectedVersion;

    public long CurrentVersion { get; } = currentVersion;
}

public sealed class AggregateNotFoundException(Guid aggregateId)
    : Exception("The aggregate does not exist.")
{
    public Guid AggregateId { get; } = aggregateId;
}
