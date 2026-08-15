using Vtt.EventSourcing;

namespace Vtt.EngineeringFixture.Domain;

public sealed class ProbeAggregate
{
    public ProbeAggregate()
    {
    }

    public Guid Id { get; private set; }

    public long Version { get; private set; }

    public int Value { get; private set; }

    public EventDecision DecideIncrement(
        Guid probeId,
        int amount,
        DateTimeOffset occurredAt)
    {
        if (probeId == Guid.Empty)
        {
            throw new ProbeInvariantException("probe_id_required");
        }

        if (amount is < 1 or > 1000)
        {
            throw new ProbeInvariantException("increment_out_of_range");
        }

        if (Value > 1_000_000 - amount)
        {
            throw new ProbeInvariantException("probe_value_limit_exceeded");
        }

        return EventDecision.Emit(
            new ProbeIncremented(
                probeId,
                amount,
                checked(Value + amount),
                checked(Version + 1),
                occurredAt));
    }

    public void Fold(ProbeIncremented @event)
    {
        Id = @event.ProbeId;
        Value = @event.ResultingValue;
        Version = @event.AggregateVersion;
    }
}

public sealed record ProbeIncremented(
    Guid ProbeId,
    int Amount,
    int ResultingValue,
    long AggregateVersion,
    DateTimeOffset OccurredAt) : IDomainEvent;

public sealed class ProbeInvariantException(string code)
    : Exception("The neutral engineering probe rejected the operation.")
{
    public string Code { get; } = code;
}
