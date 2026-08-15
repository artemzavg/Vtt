using System.Diagnostics;
using System.Text.Json;
using JasperFx.Events;
using Marten;
using Microsoft.Extensions.Logging;
using Vtt.EngineeringFixture.Application;
using Vtt.EngineeringFixture.Domain;
using Vtt.EventSourcing;
using Vtt.Messaging;
using Vtt.Persistence.Marten;

namespace Vtt.EngineeringFixture.Infrastructure;

public sealed partial class MartenProbeCommandStore(
    IDocumentStore documentStore,
    TimeProvider timeProvider,
    ILogger<MartenProbeCommandStore> logger) : IProbeCommandStore
{
    private static readonly System.Diagnostics.Metrics.Meter Meter = new("Vtt.EventStore");
    private static readonly System.Diagnostics.Metrics.Counter<long> Conflicts = Meter.CreateCounter<long>(
        "vtt.event_store.conflicts");
    private const string RouteTemplate = "/platform/probes/{probeId}/increments";
    private const string Producer = "engineering-fixture";
    private const string AggregateType = "EngineeringProbe";
    private const string EventType = "EngineeringProbeIncremented.v1";
    internal static readonly string Subject = IntegrationSubject.Create(
        "engineering-fixture",
        "engineering-probe",
        "incremented",
        1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IncrementProbeResult> IncrementAsync(
        IncrementProbeCommand command,
        CancellationToken cancellationToken)
    {
        var idempotencyId = CreateIdempotencyId(command);
        var expectedVersion = command.Metadata.ExpectedVersion
            ?? throw new InvalidOperationException("Expected version must be validated before persistence.");

        try
        {
            await using var session = documentStore.LightweightSession();
            var existing = await session.LoadAsync<IdempotencyRecord>(
                idempotencyId,
                cancellationToken);
            if (existing is not null)
            {
                return Replay(existing, command.RequestHash, command.Metadata.CommandId);
            }

            IEventStream<ProbeAggregate>? writableStream = null;
            ProbeSnapshot? currentSnapshot = null;
            ProbeAggregate aggregate;
            if (expectedVersion == 0)
            {
                aggregate = await ProbeEventStream.LoadAsync(
                    session,
                    command.ProbeId,
                    cancellationToken);
            }
            else
            {
                writableStream = await session.Events.FetchForWriting<ProbeAggregate>(
                    command.ProbeId,
                    cancellationToken);
                aggregate = writableStream.Aggregate
                    ?? throw new AggregateNotFoundException(command.ProbeId);
                currentSnapshot = await session.LoadAsync<ProbeSnapshot>(
                    command.ProbeId,
                    cancellationToken)
                    ?? throw new InvalidOperationException(
                        "The engineering stream snapshot is missing.");
            }

            if (aggregate.Version != expectedVersion)
            {
                RecordConflict();
                throw new AggregateConflictException(
                    command.ProbeId,
                    expectedVersion,
                    aggregate.Version);
            }

            var occurredAt = timeProvider.GetUtcNow();
            var decision = aggregate.DecideIncrement(command.ProbeId, command.Amount, occurredAt);
            var domainEvent = (ProbeIncremented)decision.Events.Single();
            var eventId = Guid.CreateVersion7();
            var envelope = CreateEnvelope(command, domainEvent, eventId, occurredAt);
            var response = new IncrementProbeResult(
                command.ProbeId,
                domainEvent.ResultingValue,
                domainEvent.AggregateVersion,
                eventId,
                ProjectionPending: true,
                IdempotencyReplayed: false);

            var events = decision.Events.Cast<object>().ToArray();
            if (expectedVersion == 0)
            {
                session.Events.StartStream<ProbeAggregate>(command.ProbeId, events);
            }
            else
            {
                writableStream!.AppendMany(events);
            }
            var snapshot = currentSnapshot ?? new ProbeSnapshot();
            snapshot.Id = command.ProbeId;
            snapshot.Value = domainEvent.ResultingValue;
            snapshot.Version = domainEvent.AggregateVersion;
            snapshot.UpdatedAt = occurredAt;
            if (currentSnapshot is null)
            {
                session.Insert(snapshot);
            }
            else
            {
                session.Store(snapshot);
            }

            session.Insert(OutboxMessage.Pending(
                eventId,
                Subject,
                EventEnvelopeJson.Serialize(envelope),
                envelope.TraceParent,
                occurredAt));
            session.Insert(CreateIdempotencyRecord(idempotencyId, command, response, occurredAt));

            await session.SaveChangesAsync(cancellationToken);
            return response;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogPersistenceRecovery(logger, exception.GetType().Name);
            var replay = await TryReplayAfterRaceAsync(
                idempotencyId,
                command.RequestHash,
                command.Metadata.CommandId,
                cancellationToken);
            if (replay is not null)
            {
                LogCommittedCommandRecovered(logger);
                return replay;
            }

            LogCommittedCommandNotVisible(logger);

            var currentVersion = await CurrentVersionAsync(command.ProbeId, cancellationToken);
            if (currentVersion != expectedVersion)
            {
                RecordConflict();
                throw new AggregateConflictException(
                    command.ProbeId,
                    expectedVersion,
                    currentVersion);
            }

            throw;
        }
    }

    private static EventEnvelope CreateEnvelope(
        IncrementProbeCommand command,
        ProbeIncremented domainEvent,
        Guid eventId,
        DateTimeOffset occurredAt) => new(
        eventId,
        EventType,
        occurredAt,
        Producer,
        new AggregateReference(
            AggregateType,
            command.ProbeId,
            domainEvent.AggregateVersion),
        command.Metadata.TenantId,
        SubjectId: null,
        command.Metadata.CorrelationId,
        command.Metadata.CommandId.ToString("D"),
        SchemaVersion: 1,
        Activity.Current?.Id,
        JsonSerializer.SerializeToElement(domainEvent, JsonOptions),
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["fixture"] = "step-02",
        });

    private static IdempotencyRecord CreateIdempotencyRecord(
        string id,
        IncrementProbeCommand command,
        IncrementProbeResult response,
        DateTimeOffset occurredAt) => new()
        {
            Id = id,
            PrincipalHash = StableHash.Sha256(command.Metadata.PrincipalId),
            Route = RouteTemplate,
            KeyHash = StableHash.Sha256(command.Metadata.IdempotencyKey!),
            RequestHash = command.RequestHash,
            CommandId = command.Metadata.CommandId,
            ResponseStatusCode = 200,
            ResponseBody = JsonSerializer.Serialize(response, JsonOptions),
            AggregateVersion = response.AggregateVersion,
            CreatedAt = occurredAt,
            ExpiresAt = occurredAt.AddHours(24),
        };

    private static string CreateIdempotencyId(IncrementProbeCommand command) =>
        StableHash.Sha256(
            $"{command.Metadata.PrincipalId}|{RouteTemplate}|{command.Metadata.IdempotencyKey}");

    private static IncrementProbeResult Replay(
        IdempotencyRecord record,
        string requestHash,
        Guid commandId)
    {
        if (!StableHash.FixedTimeEquals(record.RequestHash, requestHash))
        {
            throw new IdempotencyPayloadMismatchException();
        }

        var result = JsonSerializer.Deserialize<IncrementProbeResult>(
            record.ResponseBody,
            JsonOptions)
            ?? throw new JsonException("Stored idempotency response is invalid.");
        return result with { IdempotencyReplayed = record.CommandId != commandId };
    }

    private async Task<IncrementProbeResult?> TryReplayAfterRaceAsync(
        string idempotencyId,
        string requestHash,
        Guid commandId,
        CancellationToken cancellationToken)
    {
        await using var session = documentStore.QuerySession();
        var existing = await session.LoadAsync<IdempotencyRecord>(
            idempotencyId,
            cancellationToken);
        return existing is null
            ? null
            : Replay(existing, requestHash, commandId);
    }

    private async Task<long> CurrentVersionAsync(
        Guid aggregateId,
        CancellationToken cancellationToken)
    {
        await using var session = documentStore.QuerySession();
        var events = await session.Events.FetchStreamAsync(
            aggregateId,
            token: cancellationToken);
        return events.Count == 0 ? 0 : events[^1].Version;
    }

    private static void RecordConflict() =>
        Conflicts.Add(
            1,
            new KeyValuePair<string, object?>("aggregate_type", AggregateType));

    [LoggerMessage(
        EventId = 2101,
        Level = LogLevel.Warning,
        Message = "Probe command persistence reported {ErrorType}; checking the idempotency record.")]
    private static partial void LogPersistenceRecovery(ILogger logger, string errorType);

    [LoggerMessage(
        EventId = 2102,
        Level = LogLevel.Information,
        Message = "Recovered a committed probe command from its idempotency record.")]
    private static partial void LogCommittedCommandRecovered(ILogger logger);

    [LoggerMessage(
        EventId = 2103,
        Level = LogLevel.Warning,
        Message = "No committed idempotency record was visible during command recovery.")]
    private static partial void LogCommittedCommandNotVisible(ILogger logger);

}
