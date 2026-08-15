using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Vtt.Cqrs;

public sealed record CommandMetadata(
    Guid CommandId,
    string PrincipalId,
    string TenantId,
    string CorrelationId,
    string? CausationId,
    DateTimeOffset? Deadline,
    string? IdempotencyKey,
    long? ExpectedVersion);

public interface ICommand<out TResult>
{
    CommandMetadata Metadata { get; }
}

public interface IQuery<out TResult>;

public interface ICommandHandler<in TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    Task<TResult> HandleAsync(TCommand command, CancellationToken cancellationToken);
}

public interface IQueryHandler<in TQuery, TResult>
    where TQuery : IQuery<TResult>
{
    Task<TResult> HandleAsync(TQuery query, CancellationToken cancellationToken);
}

public interface ICommandValidator<in TCommand>
{
    ValueTask<IReadOnlyCollection<CommandValidationError>> ValidateAsync(
        TCommand command,
        CancellationToken cancellationToken);
}

public sealed record CommandValidationError(string Field, string Code, string Message);

public sealed class CommandValidationException(
    IReadOnlyCollection<CommandValidationError> errors)
    : Exception("The command failed validation.")
{
    public IReadOnlyCollection<CommandValidationError> Errors { get; } = errors;
}

public sealed class CommandDeadlineExceededException(DateTimeOffset deadline)
    : Exception("The command deadline has elapsed.")
{
    public DateTimeOffset Deadline { get; } = deadline;
}

public sealed class CommandPipeline<TCommand, TResult>(
    ICommandHandler<TCommand, TResult> handler,
    IEnumerable<ICommandValidator<TCommand>> validators,
    TimeProvider timeProvider)
    where TCommand : ICommand<TResult>
{
    private static readonly ActivitySource ActivitySource = new("Vtt.Cqrs");
    private static readonly Meter Meter = new("Vtt.Cqrs");
    private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>(
        "vtt.command.duration",
        "ms",
        "Duration of command handling.");
    private static readonly Counter<long> Results = Meter.CreateCounter<long>(
        "vtt.command.results",
        description: "Command results grouped by bounded result labels.");

    public async Task<TResult> ExecuteAsync(
        TCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var commandName = typeof(TCommand).Name;
        using var activity = ActivitySource.StartActivity(
            $"command {commandName}",
            ActivityKind.Internal);
        activity?.SetTag("vtt.command.type", commandName);
        activity?.SetTag("vtt.command.id", command.Metadata.CommandId.ToString("D"));

        var started = Stopwatch.GetTimestamp();
        var result = "success";

        try
        {
            using var deadlineSource = CreateDeadlineSource(command.Metadata.Deadline, cancellationToken);
            var effectiveToken = deadlineSource?.Token ?? cancellationToken;
            var errors = new List<CommandValidationError>();

            foreach (var validator in validators)
            {
                errors.AddRange(await validator.ValidateAsync(command, effectiveToken));
            }

            if (errors.Count > 0)
            {
                throw new CommandValidationException(errors);
            }

            return await handler.HandleAsync(command, effectiveToken);
        }
        catch (Exception exception)
        {
            result = exception switch
            {
                CommandValidationException => "validation_error",
                CommandDeadlineExceededException or OperationCanceledException => "cancelled",
                _ => "error",
            };
            activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
            throw;
        }
        finally
        {
            var tags = new TagList
            {
                { "command.type", commandName },
                { "result", result },
            };
            Duration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds, tags);
            Results.Add(1, tags);
        }
    }

    private CancellationTokenSource? CreateDeadlineSource(
        DateTimeOffset? deadline,
        CancellationToken cancellationToken)
    {
        if (deadline is null)
        {
            return null;
        }

        var remaining = deadline.Value - timeProvider.GetUtcNow();
        if (remaining <= TimeSpan.Zero)
        {
            throw new CommandDeadlineExceededException(deadline.Value);
        }

        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        source.CancelAfter(remaining);
        return source;
    }
}

public sealed class QueryPipeline<TQuery, TResult>(
    IQueryHandler<TQuery, TResult> handler)
    where TQuery : IQuery<TResult>
{
    public Task<TResult> ExecuteAsync(
        TQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return handler.HandleAsync(query, cancellationToken);
    }
}
