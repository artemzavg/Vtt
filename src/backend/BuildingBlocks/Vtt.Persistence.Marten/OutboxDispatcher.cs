using System.Diagnostics;
using System.Diagnostics.Metrics;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vtt.Messaging;

namespace Vtt.Persistence.Marten;

public sealed class OutboxOptions
{
    public int BatchSize { get; set; } = 50;

    public int MaxAttempts { get; set; } = 20;

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMilliseconds(250);

    public TimeSpan MaximumBackoff { get; set; } = TimeSpan.FromSeconds(30);
}

public interface IOutboxDispatcher
{
    Task<int> DispatchBatchAsync(CancellationToken cancellationToken);

    Task RetryAsync(Guid outboxMessageId, CancellationToken cancellationToken);
}

public sealed class MartenOutboxDispatcher(
    IDocumentStore documentStore,
    IIntegrationEventPublisher publisher,
    IOptions<OutboxOptions> options,
    TimeProvider timeProvider,
    ILogger<MartenOutboxDispatcher> logger) : IOutboxDispatcher
{
    private static readonly ActivitySource ActivitySource = new("Vtt.Outbox");
    private static readonly Meter Meter = new("Vtt.Outbox");
    private static readonly Counter<long> DispatchResults = Meter.CreateCounter<long>(
        "vtt.outbox.dispatch.results");
    private static readonly Histogram<double> MessageAge = Meter.CreateHistogram<double>(
        "vtt.outbox.message.age",
        "s");
    private readonly OutboxOptions _options = options.Value;

    public async Task<int> DispatchBatchAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        await using var querySession = documentStore.QuerySession();
        var pending = await querySession.Query<OutboxMessage>()
            .Where(message =>
                message.Status == OutboxStatus.Pending &&
                message.NextAttemptAt <= now)
            .OrderBy(message => message.OccurredAt)
            .Take(_options.BatchSize)
            .ToListAsync(cancellationToken);

        foreach (var candidate in pending)
        {
            await DispatchOneAsync(candidate.Id, cancellationToken);
        }

        return pending.Count;
    }

    public async Task RetryAsync(
        Guid outboxMessageId,
        CancellationToken cancellationToken)
    {
        await using var session = documentStore.LightweightSession();
        var message = await session.LoadAsync<OutboxMessage>(
            outboxMessageId,
            cancellationToken);

        if (message is null)
        {
            throw new KeyNotFoundException("The outbox message does not exist.");
        }

        message.Status = OutboxStatus.Pending;
        message.AttemptCount = 0;
        message.NextAttemptAt = timeProvider.GetUtcNow();
        message.LastErrorCode = null;
        session.Store(message);
        await session.SaveChangesAsync(cancellationToken);
    }

    private async Task DispatchOneAsync(
        Guid messageId,
        CancellationToken cancellationToken)
    {
        await using var session = documentStore.LightweightSession();
        var message = await session.LoadAsync<OutboxMessage>(messageId, cancellationToken);
        if (message is null || message.Status != OutboxStatus.Pending)
        {
            return;
        }

        using var activity = ActivitySource.StartActivity(
            "outbox publish",
            ActivityKind.Producer,
            ParseParentContext(message.TraceParent));
        activity?.SetTag("messaging.system", "nats");
        activity?.SetTag("messaging.destination.name", message.Subject);
        activity?.SetTag("messaging.message.id", message.EventId.ToString("D"));

        try
        {
            await publisher.PublishAsync(
                new IntegrationMessage(
                    message.EventId,
                    message.Subject,
                    message.Payload,
                    activity?.Id ?? message.TraceParent),
                cancellationToken);

            var now = timeProvider.GetUtcNow();
            message.Status = OutboxStatus.Published;
            message.PublishedAt = now;
            message.LastErrorCode = null;
            session.Store(message);
            await session.SaveChangesAsync(cancellationToken);

            DispatchResults.Add(1, new KeyValuePair<string, object?>("result", "published"));
            MessageAge.Record(Math.Max(0, (now - message.OccurredAt).TotalSeconds));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            message.AttemptCount++;
            message.LastErrorCode = exception.GetType().Name;
            message.Status = message.AttemptCount >= _options.MaxAttempts
                ? OutboxStatus.DeadLetter
                : OutboxStatus.Pending;
            message.NextAttemptAt = timeProvider.GetUtcNow() + CalculateBackoff(message.AttemptCount);
            session.Store(message);
            await session.SaveChangesAsync(cancellationToken);

            var result = message.Status == OutboxStatus.DeadLetter ? "dead_letter" : "retry";
            DispatchResults.Add(1, new KeyValuePair<string, object?>("result", result));
            OutboxLog.PublishFailed(
                logger,
                message.EventId,
                message.AttemptCount,
                result,
                message.LastErrorCode);
        }
    }

    private static ActivityContext ParseParentContext(string? traceParent) =>
        ActivityContext.TryParse(traceParent, traceState: null, out var context)
            ? context
            : default;

    private TimeSpan CalculateBackoff(int attempts)
    {
        var seconds = Math.Min(
            _options.MaximumBackoff.TotalSeconds,
            Math.Pow(2, Math.Min(attempts - 1, 10)));
        return TimeSpan.FromSeconds(seconds);
    }
}

public sealed class OutboxRelayWorker(
    IOutboxDispatcher dispatcher,
    IOptions<OutboxOptions> options,
    ILogger<OutboxRelayWorker> logger) : BackgroundService
{
    private readonly OutboxOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var dispatched = await dispatcher.DispatchBatchAsync(stoppingToken);
                if (dispatched == 0)
                {
                    await Task.Delay(_options.PollInterval, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                OutboxLog.RelayIterationFailed(
                    logger,
                    exception.GetType().Name,
                    exception);
                await Task.Delay(_options.PollInterval, stoppingToken);
            }
        }
    }
}

internal static partial class OutboxLog
{
    [LoggerMessage(
        EventId = 2001,
        Level = LogLevel.Warning,
        Message = "Outbox publish failed for event {EventId}; attempt {AttemptCount}; result {Result}; error {ErrorCode}.")]
    public static partial void PublishFailed(
        ILogger logger,
        Guid eventId,
        int attemptCount,
        string result,
        string? errorCode);

    [LoggerMessage(
        EventId = 2002,
        Level = LogLevel.Error,
        Message = "Outbox relay iteration failed with {ErrorCode}.")]
    public static partial void RelayIterationFailed(
        ILogger logger,
        string errorCode,
        Exception exception);
}

public static class OutboxServiceCollectionExtensions
{
    public static IServiceCollection AddVttOutboxRelay(
        this IServiceCollection services,
        Action<OutboxOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddOptions<OutboxOptions>();
        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IOutboxDispatcher, MartenOutboxDispatcher>();
        services.AddHostedService<OutboxRelayWorker>();
        return services;
    }
}
