using System.Text.Json;
using Vtt.Messaging;

namespace Vtt.EngineeringFixture.UnitTests;

public sealed class MessagingContractTests
{
    [Fact]
    public void SubjectIsVersionedAndLowCardinality()
    {
        var subject = IntegrationSubject.Create(
            "engineering-fixture",
            "engineering-probe",
            "incremented",
            1);

        Assert.Equal("vtt.engineering-fixture.engineering-probe.incremented.v1", subject);
        Assert.Throws<ArgumentException>(() =>
            IntegrationSubject.Create("EngineeringFixture", "probe", "changed", 1));
    }

    [Fact]
    public void EnvelopeRoundTripsWithoutLosingAggregateVersion()
    {
        var id = Guid.CreateVersion7();
        var envelope = new EventEnvelope(
            Guid.CreateVersion7(),
            "EngineeringProbeIncremented.v1",
            DateTimeOffset.UnixEpoch,
            "engineering-fixture",
            new AggregateReference("EngineeringProbe", id, 3),
            "tenant",
            null,
            "correlation",
            "causation",
            1,
            "00-0123456789abcdef0123456789abcdef-0123456789abcdef-01",
            JsonSerializer.SerializeToElement(new { probeId = id, resultingValue = 9 }),
            new Dictionary<string, string> { ["fixture"] = "step-02" });

        var restored = EventEnvelopeJson.Deserialize(EventEnvelopeJson.Serialize(envelope));

        Assert.Equal(envelope.EventId, restored.EventId);
        Assert.Equal(3, restored.Aggregate.Version);
        Assert.Equal("step-02", restored.Metadata["fixture"]);
    }
}
