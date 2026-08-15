using Vtt.Cqrs;
using Vtt.EngineeringFixture.Application;

namespace Vtt.EngineeringFixture.UnitTests;

public sealed class CqrsPipelineTests
{
    [Fact]
    public async Task ValidationStopsHandler()
    {
        var handler = new RecordingHandler();
        var pipeline = new CommandPipeline<TestCommand, int>(
            handler,
            [new RejectingValidator()],
            new FixedTimeProvider(DateTimeOffset.UnixEpoch));
        var command = new TestCommand(Metadata(deadline: null));

        var exception = await Assert.ThrowsAsync<CommandValidationException>(() =>
            pipeline.ExecuteAsync(command));

        Assert.Equal("rejected", Assert.Single(exception.Errors).Code);
        Assert.False(handler.WasCalled);
    }

    [Fact]
    public async Task ElapsedDeadlineStopsHandler()
    {
        var handler = new RecordingHandler();
        var pipeline = new CommandPipeline<TestCommand, int>(
            handler,
            [],
            new FixedTimeProvider(DateTimeOffset.UnixEpoch.AddMinutes(1)));
        var command = new TestCommand(Metadata(DateTimeOffset.UnixEpoch));

        await Assert.ThrowsAsync<CommandDeadlineExceededException>(() =>
            pipeline.ExecuteAsync(command));

        Assert.False(handler.WasCalled);
    }

    [Fact]
    public async Task IdempotentHandlerRetriesOneUncertainFailure()
    {
        var store = new FailOnceProbeStore();
        var handler = new IncrementProbeHandler(store);
        var command = new IncrementProbeCommand(
            Guid.CreateVersion7(),
            1,
            "request-hash",
            Metadata(deadline: null));

        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.Equal(2, store.Attempts);
        Assert.Equal(1, result.AggregateVersion);
    }

    private static CommandMetadata Metadata(DateTimeOffset? deadline) => new(
        Guid.CreateVersion7(),
        "principal",
        "tenant",
        "correlation",
        null,
        deadline,
        "key",
        0);

    private sealed record TestCommand(CommandMetadata Metadata) : ICommand<int>;

    private sealed class RecordingHandler : ICommandHandler<TestCommand, int>
    {
        public bool WasCalled { get; private set; }

        public Task<int> HandleAsync(TestCommand command, CancellationToken cancellationToken)
        {
            WasCalled = true;
            return Task.FromResult(42);
        }
    }

    private sealed class RejectingValidator : ICommandValidator<TestCommand>
    {
        public ValueTask<IReadOnlyCollection<CommandValidationError>> ValidateAsync(
            TestCommand command,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyCollection<CommandValidationError>>(
                [new("field", "rejected", "Rejected for the test.")]);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FailOnceProbeStore : IProbeCommandStore
    {
        public int Attempts { get; private set; }

        public Task<IncrementProbeResult> IncrementAsync(
            IncrementProbeCommand command,
            CancellationToken cancellationToken)
        {
            Attempts++;
            if (Attempts == 1)
            {
                throw new InvalidOperationException("Simulated uncertain commit result.");
            }

            return Task.FromResult(new IncrementProbeResult(
                command.ProbeId,
                command.Amount,
                1,
                Guid.CreateVersion7(),
                ProjectionPending: true,
                IdempotencyReplayed: false));
        }
    }
}
