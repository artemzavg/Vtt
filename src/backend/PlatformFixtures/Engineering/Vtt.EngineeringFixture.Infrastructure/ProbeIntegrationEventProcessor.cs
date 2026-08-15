using System.Diagnostics.Metrics;
using System.Text.Json;
using Marten;
using Vtt.EngineeringFixture.Domain;
using Vtt.Messaging;
using Vtt.Persistence.Marten;

namespace Vtt.EngineeringFixture.Infrastructure;

public sealed class ProbeIntegrationEventProcessor(
    IDocumentStore documentStore,
    TimeProvider timeProvider,
    Microsoft.Extensions.Logging.ILogger<ProbeIntegrationEventProcessor> logger)
{
    public const string ConsumerName = "engineering-probe-projection-v1";
    private const string ExpectedEventType = "EngineeringProbeIncremented.v1";
    private static readonly Meter Meter = new("Vtt.Inbox");
    private static readonly Counter<long> Results = Meter.CreateCounter<long>(
        "vtt.inbox.results");
    private static readonly Histogram<long> ProjectionLag = Meter.CreateHistogram<long>(
        "vtt.projection.version_lag");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<InboxOutcome> ProcessAsync(
        string subject,
        string payload,
        long streamSequence,
        CancellationToken cancellationToken)
    {
        EventEnvelope envelope;
        try
        {
            envelope = EventEnvelopeJson.Deserialize(payload);
        }
        catch (JsonException)
        {
            await QuarantineAsync(
                subject,
                payload,
                eventId: null,
                "malformed_event_envelope",
                cancellationToken);
            Record(InboxOutcome.Quarantined);
            return InboxOutcome.Quarantined;
        }

        var inboxId = $"{ConsumerName}:{envelope.EventId:N}";
        await using var session = documentStore.LightweightSession();
        if (await session.LoadAsync<InboxRecord>(inboxId, cancellationToken) is not null)
        {
            Record(InboxOutcome.Duplicate);
            return InboxOutcome.Duplicate;
        }

        if (envelope.EventType != ExpectedEventType || envelope.SchemaVersion != 1)
        {
            await StoreQuarantineInSessionAsync(
                session,
                subject,
                payload,
                envelope.EventId,
                "unsupported_event_contract",
                cancellationToken);
            session.Insert(CreateInbox(envelope, inboxId, InboxOutcome.Quarantined));
            await session.SaveChangesAsync(cancellationToken);
            Record(InboxOutcome.Quarantined);
            return InboxOutcome.Quarantined;
        }

        ProbeIncremented integrationEvent;
        try
        {
            integrationEvent = envelope.Data.Deserialize<ProbeIncremented>(JsonOptions)
                ?? throw new JsonException("Event data is empty.");
        }
        catch (JsonException)
        {
            await StoreQuarantineInSessionAsync(
                session,
                subject,
                payload,
                envelope.EventId,
                "malformed_event_data",
                cancellationToken);
            session.Insert(CreateInbox(envelope, inboxId, InboxOutcome.Quarantined));
            await session.SaveChangesAsync(cancellationToken);
            Record(InboxOutcome.Quarantined);
            return InboxOutcome.Quarantined;
        }

        if (integrationEvent.ProbeId != envelope.Aggregate.Id ||
            integrationEvent.AggregateVersion != envelope.Aggregate.Version)
        {
            await StoreQuarantineInSessionAsync(
                session,
                subject,
                payload,
                envelope.EventId,
                "event_envelope_mismatch",
                cancellationToken);
            session.Insert(CreateInbox(envelope, inboxId, InboxOutcome.Quarantined));
            await session.SaveChangesAsync(cancellationToken);
            Record(InboxOutcome.Quarantined);
            return InboxOutcome.Quarantined;
        }

        var projection = await session.LoadAsync<ProbeProjection>(
            envelope.Aggregate.Id,
            cancellationToken);
        var currentVersion = projection?.SourceAggregateVersion ?? 0;
        InboxOutcome outcome;

        if (envelope.Aggregate.Version <= currentVersion)
        {
            outcome = InboxOutcome.IgnoredOld;
        }
        else if (envelope.Aggregate.Version == currentVersion + 1)
        {
            projection ??= new ProbeProjection { Id = envelope.Aggregate.Id };
            projection.Value = integrationEvent.ResultingValue;
            projection.ProjectionVersion = envelope.Aggregate.Version;
            projection.SourceAggregateVersion = envelope.Aggregate.Version;
            projection.AsOf = envelope.OccurredAt;
            projection.Checksum = StableHash.Projection(
                projection.Id,
                projection.Value,
                projection.SourceAggregateVersion);
            session.Store(projection);
            outcome = InboxOutcome.Applied;
        }
        else
        {
            EngineeringLog.ProjectionGapDetected(
                logger,
                envelope.Aggregate.Id,
                currentVersion,
                envelope.Aggregate.Version);
            var aggregate = await ProbeEventStream.LoadAsync(
                session,
                envelope.Aggregate.Id,
                cancellationToken);
            if (aggregate.Version == 0)
            {
                throw new InvalidOperationException("Gap resync could not load the source stream.");
            }
            projection = new ProbeProjection
            {
                Id = aggregate.Id,
                Value = aggregate.Value,
                ProjectionVersion = aggregate.Version,
                SourceAggregateVersion = aggregate.Version,
                AsOf = timeProvider.GetUtcNow(),
                Checksum = StableHash.Projection(aggregate.Id, aggregate.Value, aggregate.Version),
            };
            session.Store(projection);
            outcome = InboxOutcome.ResyncedGap;
        }

        session.Insert(CreateInbox(envelope, inboxId, outcome));
        var checkpoint = await session.LoadAsync<ProjectionCheckpoint>(
            ConsumerName,
            cancellationToken) ?? new ProjectionCheckpoint { Id = ConsumerName };
        checkpoint.LastSequence = Math.Max(checkpoint.LastSequence, streamSequence);
        checkpoint.UpdatedAt = timeProvider.GetUtcNow();
        session.Store(checkpoint);
        await session.SaveChangesAsync(cancellationToken);

        ProjectionLag.Record(Math.Max(0, envelope.Aggregate.Version - currentVersion));
        Record(outcome);
        return outcome;
    }

    private InboxRecord CreateInbox(
        EventEnvelope envelope,
        string inboxId,
        InboxOutcome outcome) => new()
        {
            Id = inboxId,
            Consumer = ConsumerName,
            EventId = envelope.EventId,
            AggregateId = envelope.Aggregate.Id,
            AggregateVersion = envelope.Aggregate.Version,
            Outcome = outcome,
            ProcessedAt = timeProvider.GetUtcNow(),
        };

    private async Task QuarantineAsync(
        string subject,
        string payload,
        Guid? eventId,
        string reasonCode,
        CancellationToken cancellationToken)
    {
        await using var session = documentStore.LightweightSession();
        await StoreQuarantineInSessionAsync(
            session,
            subject,
            payload,
            eventId,
            reasonCode,
            cancellationToken);
        await session.SaveChangesAsync(cancellationToken);
    }

    private Task StoreQuarantineInSessionAsync(
        IDocumentSession session,
        string subject,
        string payload,
        Guid? eventId,
        string reasonCode,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        session.Insert(new QuarantinedMessage
        {
            Id = Guid.CreateVersion7(),
            EventId = eventId,
            Consumer = ConsumerName,
            Subject = subject,
            ReasonCode = reasonCode,
            Payload = payload,
            QuarantinedAt = timeProvider.GetUtcNow(),
        });
        return Task.CompletedTask;
    }

    private static void Record(InboxOutcome outcome) =>
        Results.Add(1, new KeyValuePair<string, object?>("result", outcome.ToString()));
}
