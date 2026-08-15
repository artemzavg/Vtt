using Marten;
using Vtt.EngineeringFixture.Application;
using Vtt.Messaging;
using Vtt.Persistence.Marten;

namespace Vtt.EngineeringFixture.Infrastructure;

public sealed class MessageOperations(
    IDocumentStore documentStore,
    IIntegrationEventPublisher publisher,
    IOutboxDispatcher outboxDispatcher,
    TimeProvider timeProvider) : IMessageOperations
{
    public async Task<IReadOnlyList<QuarantineResult>> ListQuarantineAsync(
        CancellationToken cancellationToken)
    {
        await using var session = documentStore.QuerySession();
        var records = await session.Query<QuarantinedMessage>()
            .OrderByDescending(message => message.QuarantinedAt)
            .Take(100)
            .ToListAsync(cancellationToken);
        return records.Select(Map).ToArray();
    }

    public async Task RetryQuarantinedAsync(
        Guid quarantineId,
        CancellationToken cancellationToken)
    {
        string subject;
        string payload;
        await using (var session = documentStore.LightweightSession())
        {
            var message = await session.LoadAsync<QuarantinedMessage>(
                quarantineId,
                cancellationToken)
                ?? throw new KeyNotFoundException("The quarantined message does not exist.");

            if (message.EventId is { } eventId)
            {
                session.Delete<InboxRecord>(
                    $"{message.Consumer}:{eventId:N}");
            }

            message.RetryCount++;
            message.RetriedAt = timeProvider.GetUtcNow();
            subject = message.Subject;
            payload = message.Payload;
            session.Store(message);
            await session.SaveChangesAsync(cancellationToken);
        }

        // A new transport id bypasses the JetStream duplicate window. The canonical
        // event id inside the envelope remains unchanged for consumer idempotency.
        await publisher.PublishAsync(
            new IntegrationMessage(
                Guid.CreateVersion7(),
                subject,
                payload,
                TraceParent: null),
            cancellationToken);
    }

    public Task RetryOutboxAsync(
        Guid outboxMessageId,
        CancellationToken cancellationToken) =>
        outboxDispatcher.RetryAsync(outboxMessageId, cancellationToken);

    private static QuarantineResult Map(QuarantinedMessage message) => new(
        message.Id,
        message.EventId,
        message.Consumer,
        message.Subject,
        message.ReasonCode,
        message.RetryCount,
        message.QuarantinedAt,
        message.RetriedAt);
}
