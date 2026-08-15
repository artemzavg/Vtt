using Vtt.Cqrs;

namespace Vtt.EngineeringFixture.Application;

public sealed record IncrementProbeCommand(
    Guid ProbeId,
    int Amount,
    string RequestHash,
    CommandMetadata Metadata) : ICommand<IncrementProbeResult>;

public sealed record IncrementProbeResult(
    Guid ProbeId,
    int Value,
    long AggregateVersion,
    Guid EventId,
    bool ProjectionPending,
    bool IdempotencyReplayed);

public interface IProbeCommandStore
{
    Task<IncrementProbeResult> IncrementAsync(
        IncrementProbeCommand command,
        CancellationToken cancellationToken);
}

public sealed class IncrementProbeHandler(IProbeCommandStore store)
    : ICommandHandler<IncrementProbeCommand, IncrementProbeResult>
{
    public async Task<IncrementProbeResult> HandleAsync(
        IncrementProbeCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            return await store.IncrementAsync(command, cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // One bounded retry is safe because this command always carries an
            // idempotency key, a stable command id, and an expected stream version.
            return await store.IncrementAsync(command, cancellationToken);
        }
    }
}

public sealed class IncrementProbeValidator
    : ICommandValidator<IncrementProbeCommand>
{
    public ValueTask<IReadOnlyCollection<CommandValidationError>> ValidateAsync(
        IncrementProbeCommand command,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var errors = new List<CommandValidationError>();

        if (command.ProbeId == Guid.Empty)
        {
            errors.Add(new("probeId", "probe_id_required", "Probe id is required."));
        }

        if (command.Amount is < 1 or > 1000)
        {
            errors.Add(new(
                "amount",
                "increment_out_of_range",
                "Amount must be between 1 and 1000."));
        }

        if (string.IsNullOrWhiteSpace(command.Metadata.IdempotencyKey))
        {
            errors.Add(new(
                "Idempotency-Key",
                "idempotency_key_required",
                "Idempotency-Key is required for commands."));
        }

        if (command.Metadata.ExpectedVersion is null or < 0)
        {
            errors.Add(new(
                "If-Match",
                "expected_version_required",
                "A non-negative expected version is required."));
        }

        return ValueTask.FromResult<IReadOnlyCollection<CommandValidationError>>(errors);
    }
}

public sealed class IdempotencyPayloadMismatchException()
    : Exception("The idempotency key was already used with a different request payload.");
