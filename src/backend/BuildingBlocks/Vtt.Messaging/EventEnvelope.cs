using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Vtt.Messaging;

public sealed record AggregateReference(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("version")] long Version);

public sealed record EventEnvelope(
    [property: JsonPropertyName("eventId")] Guid EventId,
    [property: JsonPropertyName("eventType")] string EventType,
    [property: JsonPropertyName("occurredAt")] DateTimeOffset OccurredAt,
    [property: JsonPropertyName("producer")] string Producer,
    [property: JsonPropertyName("aggregate")] AggregateReference Aggregate,
    [property: JsonPropertyName("tenantId")] string TenantId,
    [property: JsonPropertyName("subjectId")] string? SubjectId,
    [property: JsonPropertyName("correlationId")] string CorrelationId,
    [property: JsonPropertyName("causationId")] string CausationId,
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("traceParent")] string? TraceParent,
    [property: JsonPropertyName("data")] JsonElement Data,
    [property: JsonPropertyName("metadata")] IReadOnlyDictionary<string, string> Metadata);

public sealed record IntegrationMessage(
    Guid EventId,
    string Subject,
    string Payload,
    string? TraceParent);

public interface IIntegrationEventPublisher
{
    Task PublishAsync(IntegrationMessage message, CancellationToken cancellationToken);
}

public static partial class IntegrationSubject
{
    public static string Create(
        string producer,
        string aggregateType,
        string eventName,
        int majorVersion)
    {
        if (majorVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(majorVersion),
                majorVersion,
                "Major version must be positive.");
        }

        var tokens = new[] { producer, aggregateType, eventName };
        if (tokens.Any(token => !SubjectTokenPattern().IsMatch(token)))
        {
            throw new ArgumentException(
                "Subject tokens must use lowercase letters, digits, and hyphens only.");
        }

        return $"vtt.{producer}.{aggregateType}.{eventName}.v{majorVersion}";
    }

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]*[a-z0-9])?$", RegexOptions.CultureInvariant)]
    private static partial Regex SubjectTokenPattern();
}

public static class EventEnvelopeJson
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    public static string Serialize(EventEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        return JsonSerializer.Serialize(envelope, SerializerOptions);
    }

    public static EventEnvelope Deserialize(string payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payload);
        return JsonSerializer.Deserialize<EventEnvelope>(payload, SerializerOptions)
            ?? throw new JsonException("The event envelope was empty.");
    }
}
