using NATS.Client.Core;
using NATS.Client.JetStream;
using Vtt.Messaging;

namespace Vtt.Messaging.Nats;

public sealed class NatsMessagingOptions
{
    public string Url { get; init; } = "nats://127.0.0.1:4222";

    public string ClientName { get; init; } = "vtt-service";
}

public sealed class NatsIntegrationEventPublisher : IIntegrationEventPublisher, IAsyncDisposable
{
    private readonly NatsMessagingOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private NatsConnection _connection;
    private NatsJSContext _context;

    public NatsIntegrationEventPublisher(NatsMessagingOptions options)
    {
        _options = options;
        _connection = NatsConnectionFactory.Create(options, "outbox");
        _context = new NatsJSContext(_connection);
    }

    public async Task PublishAsync(
        IntegrationMessage message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        var headers = new NatsHeaders
        {
            ["Nats-Msg-Id"] = message.EventId.ToString("D"),
            ["Content-Type"] = "application/json",
        };

        if (!string.IsNullOrWhiteSpace(message.TraceParent))
        {
            headers["traceparent"] = message.TraceParent;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(2));
                await _context.PublishAsync(
                    message.Subject,
                    message.Payload,
                    NatsDefaultSerializer<string>.Default,
                    headers: headers,
                    cancellationToken: timeout.Token);
            }
            catch when (!cancellationToken.IsCancellationRequested)
            {
                await ResetConnectionAsync();
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync();
        _gate.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task ResetConnectionAsync()
    {
        var failedConnection = _connection;
        _connection = NatsConnectionFactory.Create(_options, "outbox");
        _context = new NatsJSContext(_connection);
        await failedConnection.DisposeAsync();
    }
}
