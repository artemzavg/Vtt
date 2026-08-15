using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using Vtt.Messaging;
using Vtt.Messaging.Nats;
using Vtt.Persistence.Marten;

namespace Vtt.EngineeringFixture.Infrastructure;

public sealed class ProbeJetStreamConsumer(
    NatsMessagingOptions messagingOptions,
    ProbeIntegrationEventProcessor processor,
    ILogger<ProbeJetStreamConsumer> logger) : BackgroundService
{
    private const string StreamName = "VTT_EVENTS";
    private static readonly ActivitySource ActivitySource = new("Vtt.Inbox");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var connection = NatsConnectionFactory.Create(
                    messagingOptions,
                    "probe-projection");
                var context = new NatsJSContext(connection);
                var consumer = await context.CreateOrUpdateConsumerAsync(
                    StreamName,
                    new ConsumerConfig(ProbeIntegrationEventProcessor.ConsumerName)
                    {
                        Description = "Step 02 effectively-once projection fixture",
                        FilterSubject = MartenProbeCommandStore.Subject,
                        MaxDeliver = 5,
                        MaxAckPending = 64,
                    },
                    stoppingToken);

                await foreach (var message in consumer.ConsumeAsync<string>(
                                   NatsDefaultSerializer<string>.Default,
                                   new NatsJSConsumeOpts
                                   {
                                       MaxMsgs = 64,
                                       Expires = TimeSpan.FromSeconds(2),
                                       IdleHeartbeat = TimeSpan.FromSeconds(1),
                                   },
                                   stoppingToken))
                {
                    await ProcessMessageAsync(message, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                EngineeringLog.ConsumerUnavailable(
                    logger,
                    exception.GetType().Name,
                    exception);
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
        }
    }

    private async Task ProcessMessageAsync(
        INatsJSMsg<string> message,
        CancellationToken cancellationToken)
    {
        var parentContext = GetParentContext(message.Headers, message.Data);
        using var activity = ActivitySource.StartActivity(
            "integration event consume",
            ActivityKind.Consumer,
            parentContext);
        activity?.SetTag("messaging.system", "nats");
        activity?.SetTag("messaging.destination.name", message.Subject);

        try
        {
            var sequence = checked((long)(message.Metadata?.Sequence.Stream ?? 0));
            var outcome = await processor.ProcessAsync(
                message.Subject,
                message.Data ?? string.Empty,
                sequence,
                cancellationToken);

            if (outcome == InboxOutcome.Quarantined)
            {
                await message.AckTerminateAsync(cancellationToken: cancellationToken);
            }
            else
            {
                await message.AckAsync(cancellationToken: cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            EngineeringLog.ConsumerProcessingFailed(
                logger,
                message.Subject,
                exception.GetType().Name,
                exception);
            await message.NakAsync(cancellationToken: cancellationToken);
        }
    }

    private static ActivityContext GetParentContext(NatsHeaders? headers, string? payload)
    {
        var transportTraceParent = headers?["traceparent"].ToString();
        if (ActivityContext.TryParse(
                transportTraceParent,
                traceState: null,
                isRemote: true,
                out var transportContext))
        {
            return transportContext;
        }

        try
        {
            var envelope = EventEnvelopeJson.Deserialize(payload ?? string.Empty);
            return ActivityContext.TryParse(
                envelope.TraceParent,
                traceState: null,
                isRemote: true,
                out var context)
                ? context
                : default;
        }
        catch (System.Text.Json.JsonException)
        {
            return default;
        }
    }
}

internal static partial class EngineeringLog
{
    [LoggerMessage(
        EventId = 2101,
        Level = LogLevel.Warning,
        Message = "JetStream consumer is unavailable; error {ErrorCode}.")]
    public static partial void ConsumerUnavailable(
        ILogger logger,
        string errorCode,
        Exception exception);

    [LoggerMessage(
        EventId = 2102,
        Level = LogLevel.Error,
        Message = "JetStream message processing failed for {Subject}; error {ErrorCode}.")]
    public static partial void ConsumerProcessingFailed(
        ILogger logger,
        string subject,
        string errorCode,
        Exception exception);

    [LoggerMessage(
        EventId = 2103,
        Level = LogLevel.Error,
        Message = "Projection rebuild {OperationId} failed; error {ErrorCode}.")]
    public static partial void ProjectionRebuildFailed(
        ILogger logger,
        Guid operationId,
        string errorCode,
        Exception exception);

    [LoggerMessage(
        EventId = 2104,
        Level = LogLevel.Warning,
        Message = "Projection worker iteration failed; error {ErrorCode}.")]
    public static partial void ProjectionWorkerIterationFailed(
        ILogger logger,
        string errorCode,
        Exception exception);

    [LoggerMessage(
        EventId = 2105,
        Level = LogLevel.Warning,
        Message = "Projection gap detected for aggregate {AggregateId}: current {CurrentVersion}, received {ReceivedVersion}; resynchronizing from the event stream.")]
    public static partial void ProjectionGapDetected(
        ILogger logger,
        Guid aggregateId,
        long currentVersion,
        long receivedVersion);
}
