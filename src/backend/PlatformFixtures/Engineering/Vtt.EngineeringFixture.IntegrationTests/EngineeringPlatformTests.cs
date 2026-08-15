using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Vtt.EngineeringFixture.Application;
using Vtt.EngineeringFixture.Contracts;
using Vtt.EngineeringFixture.Infrastructure;
using Vtt.Persistence.Marten;

namespace Vtt.EngineeringFixture.IntegrationTests;

[Collection(EngineeringPlatformDefinition.Name)]
public sealed class EngineeringPlatformTests(EngineeringPlatformFixture fixture)
{
    private static readonly TimeSpan EventuallyTimeout = TimeSpan.FromSeconds(15);
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task LiveOpenApiMatchesSourceOfTruthOperations()
    {
        using var live = JsonDocument.Parse(
            await fixture.Client.GetStringAsync("/openapi/v1.json"));
        using var source = JsonDocument.Parse(
            await File.ReadAllTextAsync(FindRepositoryFile(
                "contracts",
                "openapi",
                "engineering-fixture.v1.json")));

        Assert.Equal(
            source.RootElement.GetProperty("openapi").GetString(),
            live.RootElement.GetProperty("openapi").GetString());
        Assert.Equal(
            ReadOperations(source.RootElement),
            ReadOperations(live.RootElement));
    }

    [Fact]
    public async Task CommandCommitsEventAndOutboxAndProjectsThroughJetStream()
    {
        var probeId = Guid.CreateVersion7();

        var response = await IncrementAsync(probeId, 0, "atomic-" + probeId, 7);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<IncrementProbeResponse>();
        Assert.NotNull(body);
        Assert.Equal(1, body.AggregateVersion);
        Assert.True(body.ProjectionPending);
        Assert.False(string.IsNullOrWhiteSpace(body.CorrelationId));

        await using var scope = fixture.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IDocumentStore>();
        await using var session = store.QuerySession();
        var events = await session.Events.FetchStreamAsync(probeId);
        var outbox = await session.LoadAsync<OutboxMessage>(body.EventId);
        Assert.Single(events);
        Assert.NotNull(outbox);

        var projection = await WaitForProjectionAsync(probeId, 1);
        Assert.Equal(7, projection.Value);
        Assert.Equal(1, projection.SourceAggregateVersion);

        var next = await IncrementAsync(probeId, 1, "atomic-next-" + probeId, 3);
        Assert.Equal(HttpStatusCode.OK, next.StatusCode);
        var nextBody = await next.Content.ReadFromJsonAsync<IncrementProbeResponse>();
        Assert.NotNull(nextBody);
        Assert.Equal(2, nextBody.AggregateVersion);

        var updatedProjection = await WaitForProjectionAsync(probeId, 2);
        Assert.Equal(10, updatedProjection.Value);
        Assert.Equal(2, updatedProjection.SourceAggregateVersion);

        await using var verification = store.QuerySession();
        Assert.Equal(2, (await verification.Events.FetchStreamAsync(probeId)).Count);
        Assert.NotNull(await verification.LoadAsync<OutboxMessage>(nextBody.EventId));
    }

    [Fact]
    public async Task SameIdempotencyKeyReplaysAndDifferentPayloadConflicts()
    {
        var probeId = Guid.CreateVersion7();
        var key = "idempotent-" + probeId;
        var first = await IncrementAsync(probeId, 0, key, 4);
        var replay = await IncrementAsync(probeId, 0, key, 4);
        var mismatch = await IncrementAsync(probeId, 0, key, 5);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var firstBody = await first.Content.ReadFromJsonAsync<IncrementProbeResponse>();
        var replayBody = await replay.Content.ReadFromJsonAsync<IncrementProbeResponse>();
        Assert.Equal(firstBody?.EventId, replayBody?.EventId);
        Assert.True(replayBody?.IdempotencyReplayed);
        Assert.Equal(HttpStatusCode.Conflict, mismatch.StatusCode);
        await AssertProblemCodeAsync(mismatch, PlatformProblemCodes.IdempotencyPayloadMismatch);

        await using var scope = fixture.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IDocumentStore>();
        await using var session = store.QuerySession();
        Assert.Single(await session.Events.FetchStreamAsync(probeId));
    }

    [Fact]
    public async Task ConcurrentCommandsWithSameExpectedVersionHaveSingleWinner()
    {
        fixture.Logs.Clear();
        var probeId = Guid.CreateVersion7();
        var first = IncrementAsync(probeId, 0, "concurrent-a-" + probeId, 2);
        var second = IncrementAsync(probeId, 0, "concurrent-b-" + probeId, 3);

        var responses = await Task.WhenAll(first, second);

        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        var conflict = Assert.Single(
            responses,
            response => response.StatusCode == HttpStatusCode.Conflict);
        await AssertProblemCodeAsync(conflict, PlatformProblemCodes.AggregateConflict);

        var logs = fixture.Logs.Snapshot();
        Assert.DoesNotContain("ResultingValue", logs, StringComparison.Ordinal);
        Assert.DoesNotContain("PARAMETERS", logs, StringComparison.Ordinal);
        Assert.DoesNotContain("mt_events", logs, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OutboxSurvivesNatsOutageAndDeliversAfterRestart()
    {
        var probeId = Guid.CreateVersion7();
        await fixture.StopNatsAsync();
        try
        {
            var response = await IncrementAsync(probeId, 0, "outage-" + probeId, 6);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<IncrementProbeResponse>();
            Assert.NotNull(body);

            await Task.Delay(TimeSpan.FromMilliseconds(750));
            await using var scope = fixture.Services.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<IDocumentStore>();
            await using var session = store.QuerySession();
            var outbox = await session.LoadAsync<OutboxMessage>(body.EventId);
            Assert.NotNull(outbox);
            Assert.Equal(OutboxStatus.Pending, outbox.Status);
        }
        finally
        {
            await fixture.StartNatsAsync();
            await fixture.RestartApplicationAsync();
        }

        var projection = await WaitForProjectionAsync(probeId, 1, TimeSpan.FromSeconds(35));
        Assert.Equal(6, projection.Value);
    }

    [Fact]
    public async Task DuplicateAndPoisonMessagesAreHandledWithoutDoubleEffect()
    {
        var probeId = Guid.CreateVersion7();
        var response = await IncrementAsync(probeId, 0, "duplicate-" + probeId, 8);
        var body = await response.Content.ReadFromJsonAsync<IncrementProbeResponse>();
        Assert.NotNull(body);
        var original = await WaitForProjectionAsync(probeId, 1);

        await using var scope = fixture.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IDocumentStore>();
        var processor = scope.ServiceProvider.GetRequiredService<ProbeIntegrationEventProcessor>();
        await using var session = store.QuerySession();
        var outbox = await session.LoadAsync<OutboxMessage>(body.EventId);
        Assert.NotNull(outbox);

        var duplicate = await processor.ProcessAsync(
            outbox.Subject,
            outbox.Payload,
            streamSequence: 999,
            CancellationToken.None);
        var poison = await processor.ProcessAsync(
            outbox.Subject,
            "{not-json",
            streamSequence: 1000,
            CancellationToken.None);

        Assert.Equal(InboxOutcome.Duplicate, duplicate);
        Assert.Equal(InboxOutcome.Quarantined, poison);
        var after = await WaitForProjectionAsync(probeId, 1);
        Assert.Equal(original.Checksum, after.Checksum);

        var quarantineResponse = await fixture.Client.GetAsync("/platform/quarantine");
        quarantineResponse.EnsureSuccessStatusCode();
        var quarantine = await quarantineResponse.Content.ReadFromJsonAsync<QuarantineResponse[]>();
        var poisoned = Assert.Single(
            quarantine ?? [],
            message => message.ReasonCode == "malformed_event_envelope" &&
                       message.RetryCount == 0);

        var retry = await fixture.Client.PostAsync(
            $"/platform/quarantine/{poisoned.QuarantineId:D}/retry",
            content: null);
        Assert.Equal(HttpStatusCode.Accepted, retry.StatusCode);

        await EventuallyAsync(async () =>
        {
            var response = await fixture.Client.GetAsync("/platform/quarantine");
            var records = await response.Content.ReadFromJsonAsync<QuarantineResponse[]>();
            return records?.Any(message =>
                message.QuarantineId == poisoned.QuarantineId &&
                message.RetryCount == 1 &&
                message.RetriedAt is not null) == true;
        });
    }

    [Fact]
    public async Task OldEventIsIgnoredAndVersionGapResynchronizesFromCanonicalStream()
    {
        var probeId = Guid.CreateVersion7();
        var response = await IncrementAsync(probeId, 0, "ordering-" + probeId, 11);
        var body = await response.Content.ReadFromJsonAsync<IncrementProbeResponse>();
        Assert.NotNull(body);
        var original = await WaitForProjectionAsync(probeId, 1);

        await using var scope = fixture.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IDocumentStore>();
        var processor = scope.ServiceProvider.GetRequiredService<ProbeIntegrationEventProcessor>();
        await using var session = store.QuerySession();
        var outbox = await session.LoadAsync<OutboxMessage>(body.EventId);
        Assert.NotNull(outbox);
        var envelope = Vtt.Messaging.EventEnvelopeJson.Deserialize(outbox.Payload);

        var oldEnvelope = envelope with { EventId = Guid.CreateVersion7() };
        var oldOutcome = await processor.ProcessAsync(
            outbox.Subject,
            Vtt.Messaging.EventEnvelopeJson.Serialize(oldEnvelope),
            streamSequence: 1001,
            CancellationToken.None);

        var gapData = envelope.Data.Deserialize<
            Vtt.EngineeringFixture.Domain.ProbeIncremented>(WebJson)! with
        {
            AggregateVersion = 3,
            ResultingValue = 999,
        };
        var gapEnvelope = envelope with
        {
            EventId = Guid.CreateVersion7(),
            Aggregate = envelope.Aggregate with { Version = 3 },
            Data = JsonSerializer.SerializeToElement(
                gapData,
                WebJson),
        };
        var gapRoundTrip = gapEnvelope.Data.Deserialize<
            Vtt.EngineeringFixture.Domain.ProbeIncremented>(WebJson);
        Assert.NotNull(gapRoundTrip);
        Assert.Equal(gapEnvelope.Aggregate.Id, gapRoundTrip.ProbeId);
        Assert.Equal(gapEnvelope.Aggregate.Version, gapRoundTrip.AggregateVersion);
        var gapOutcome = await processor.ProcessAsync(
            outbox.Subject,
            Vtt.Messaging.EventEnvelopeJson.Serialize(gapEnvelope),
            streamSequence: 1002,
            CancellationToken.None);

        Assert.Equal(InboxOutcome.IgnoredOld, oldOutcome);
        if (gapOutcome == InboxOutcome.Quarantined)
        {
            var reason = await session.Query<QuarantinedMessage>()
                .Where(message => message.EventId == gapEnvelope.EventId)
                .Select(message => message.ReasonCode)
                .SingleAsync();
            Assert.Fail($"Gap fixture was quarantined as '{reason}'.");
        }
        Assert.Equal(InboxOutcome.ResyncedGap, gapOutcome);
        var after = await WaitForProjectionAsync(probeId, 1);
        Assert.Equal(original.Value, after.Value);
        Assert.Equal(original.Checksum, after.Checksum);
        Assert.Equal(1, after.SourceAggregateVersion);
    }

    [Fact]
    public async Task ProjectionRebuildIsDeterministicAndPublishesNoNewEvents()
    {
        var probeId = Guid.CreateVersion7();
        await IncrementAsync(probeId, 0, "rebuild-" + probeId, 9);
        var before = await WaitForProjectionAsync(probeId, 1);
        var outboxCountBefore = await CountOutboxAsync();

        var start = await fixture.Client.PostAsync(
            "/platform/projections/probes/rebuild",
            content: null);
        Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
        var operation = await start.Content.ReadFromJsonAsync<OperationResponse>();
        Assert.NotNull(operation);
        var completed = await WaitForOperationAsync(operation.OperationId);
        var after = await WaitForProjectionAsync(probeId, 1);

        Assert.Equal("completed", completed.Status);
        Assert.Equal(before.Checksum, after.Checksum);
        Assert.Equal(outboxCountBefore, await CountOutboxAsync());
    }

    [Fact]
    public async Task MalformedAndOversizedRequestsReturnStableProblemsWithoutPayloadEcho()
    {
        var probeId = Guid.CreateVersion7();
        using var malformed = new HttpRequestMessage(
            HttpMethod.Post,
            $"/platform/probes/{probeId:D}/increments")
        {
            Content = new StringContent("{secret-body", Encoding.UTF8, "application/json"),
        };
        malformed.Headers.TryAddWithoutValidation("If-Match", "\"0\"");
        malformed.Headers.TryAddWithoutValidation("Idempotency-Key", "malformed-" + probeId);
        var malformedResponse = await fixture.Client.SendAsync(malformed);

        Assert.Equal(HttpStatusCode.BadRequest, malformedResponse.StatusCode);
        var malformedText = await malformedResponse.Content.ReadAsStringAsync();
        Assert.Contains(PlatformProblemCodes.MalformedRequest, malformedText, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-body", malformedText, StringComparison.Ordinal);
        Assert.DoesNotContain("stack", malformedText, StringComparison.OrdinalIgnoreCase);

        using var oversized = new HttpRequestMessage(
            HttpMethod.Post,
            $"/platform/probes/{probeId:D}/increments")
        {
            Content = new StringContent(new string('x', 70_000), Encoding.UTF8, "application/json"),
        };
        oversized.Headers.TryAddWithoutValidation("If-Match", "\"0\"");
        oversized.Headers.TryAddWithoutValidation("Idempotency-Key", "oversized-" + probeId);
        var oversizedResponse = await fixture.Client.SendAsync(oversized);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversizedResponse.StatusCode);

        var unknownProperty = new HttpRequestMessage(
            HttpMethod.Post,
            $"/platform/probes/{probeId:D}/increments")
        {
            Content = new StringContent(
                "{\"amount\":1,\"unexpected\":\"secret-body\"}",
                Encoding.UTF8,
                "application/json"),
        };
        unknownProperty.Headers.TryAddWithoutValidation("If-Match", "\"0\"");
        unknownProperty.Headers.TryAddWithoutValidation("Idempotency-Key", "unknown-" + probeId);
        var unknownResponse = await fixture.Client.SendAsync(unknownProperty);
        Assert.Equal(HttpStatusCode.BadRequest, unknownResponse.StatusCode);
        Assert.DoesNotContain(
            "secret-body",
            await unknownResponse.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
    }

    private Task<HttpResponseMessage> IncrementAsync(
        Guid probeId,
        long expectedVersion,
        string idempotencyKey,
        int amount)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/platform/probes/{probeId:D}/increments")
        {
            Content = JsonContent.Create(new IncrementProbeRequest(amount)),
        };
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{expectedVersion}\"");
        request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        return fixture.Client.SendAsync(request);
    }

    private async Task<ProbeProjectionResponse> WaitForProjectionAsync(
        Guid probeId,
        long version,
        TimeSpan? timeout = null)
    {
        var deadline = DateTimeOffset.UtcNow + (timeout ?? EventuallyTimeout);
        do
        {
            var response = await fixture.Client.GetAsync(
                $"/platform/probes/{probeId:D}/projection");
            if (response.IsSuccessStatusCode)
            {
                var projection = await response.Content.ReadFromJsonAsync<ProbeProjectionResponse>();
                if (projection?.SourceAggregateVersion >= version)
                {
                    return projection;
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }
        while (DateTimeOffset.UtcNow < deadline);

        throw new TimeoutException($"Projection {probeId:D} did not reach version {version}.");
    }

    private async Task<OperationResponse> WaitForOperationAsync(Guid operationId)
    {
        var deadline = DateTimeOffset.UtcNow + EventuallyTimeout;
        do
        {
            var response = await fixture.Client.GetAsync($"/platform/operations/{operationId:D}");
            response.EnsureSuccessStatusCode();
            var operation = await response.Content.ReadFromJsonAsync<OperationResponse>();
            if (operation?.Status is "completed" or "failed" or "cancelled")
            {
                return operation;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }
        while (DateTimeOffset.UtcNow < deadline);

        throw new TimeoutException($"Operation {operationId:D} did not finish.");
    }

    private async Task<int> CountOutboxAsync()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IDocumentStore>();
        await using var session = store.QuerySession();
        return await session.Query<OutboxMessage>().CountAsync();
    }

    private static async Task AssertProblemCodeAsync(
        HttpResponseMessage response,
        string expectedCode)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(expectedCode, document.RootElement.GetProperty("code").GetString());
    }

    private static async Task EventuallyAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTimeOffset.UtcNow + EventuallyTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        throw new TimeoutException("The expected condition was not observed.");
    }

    private static string[] ReadOperations(JsonElement document) =>
        document.GetProperty("paths")
            .EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject())
            .Where(method => method.Value.TryGetProperty("operationId", out _))
            .Select(method => method.Value.GetProperty("operationId").GetString()!)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static string FindRepositoryFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. segments]);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate the repository contract.");
    }
}
