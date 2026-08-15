using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using Testcontainers.PostgreSql;

namespace Vtt.EngineeringFixture.IntegrationTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class EngineeringPlatformDefinition
    : ICollectionFixture<EngineeringPlatformFixture>
{
    public const string Name = "engineering-platform";
}

public sealed class EngineeringPlatformFixture : IAsyncLifetime
{
    private const int NatsPort = 4222;
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17.6-alpine")
        .WithDatabase("vtt_engineering")
        .WithUsername("vtt_engineering")
        .WithPassword("integration-only-password")
        .Build();
    private readonly IContainer _nats = new ContainerBuilder("nats:2.11.3-alpine")
        .WithCommand("--jetstream", "--store_dir", "/data/jetstream")
        .WithPortBinding(NatsPort, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(NatsPort))
        .Build();
    private WebApplicationFactory<Program>? _factory;

    public HttpClient Client { get; private set; } = null!;

    public IServiceProvider Services => _factory?.Services
        ?? throw new InvalidOperationException("Fixture has not started.");

    public TestLogCollector Logs { get; } = new();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await _nats.StartAsync();
        await EnsureStreamAsync();
        await StartApplicationAsync();
    }

    private async Task StartApplicationAsync()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder
                .UseSetting("VTT_ENGINEERING_CONNECTION_STRING", _postgres.GetConnectionString())
                .UseSetting("NATS_URL", NatsUrl)
                .UseSetting("VTT_APPLY_SCHEMA", "true")
                .UseSetting("OTEL_EXPORTER_OTLP_ENDPOINT", string.Empty)
                .UseSetting("VTT_READINESS_TCP_ENDPOINTS", string.Empty)
                .UseSetting("Logging:LogLevel:Marten", "Warning")
                .ConfigureLogging(logging => logging.AddProvider(Logs)));
        Client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        using var ready = await Client.GetAsync("/health/live");
        ready.EnsureSuccessStatusCode();
        var applicationNats = Services.GetRequiredService<INatsConnection>();
        await applicationNats.ConnectAsync();
        await applicationNats.PingAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(250));
    }

    public async Task DisposeAsync()
    {
        if (Client is not null)
        {
            Client.Dispose();
        }
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        await Task.WhenAll(_nats.DisposeAsync().AsTask(), _postgres.DisposeAsync().AsTask());
    }

    public Task StopNatsAsync() => _nats.StopAsync();

    public async Task StartNatsAsync()
    {
        await _nats.StartAsync();
        await EnsureStreamAsync();
    }

    public async Task RestartApplicationAsync()
    {
        Client.Dispose();
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        await StartApplicationAsync();
    }

    private string NatsUrl => $"nats://{_nats.Hostname}:{_nats.GetMappedPublicPort(NatsPort)}";

    private async Task EnsureStreamAsync()
    {
        await using var connection = new NatsConnection(new NatsOpts { Url = NatsUrl });
        var context = new NatsJSContext(connection);
        await context.CreateOrUpdateStreamAsync(
            new StreamConfig("VTT_EVENTS", ["vtt.>"]));
    }
}

public sealed class TestLogCollector : ILoggerProvider
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _entries = new();

    public ILogger CreateLogger(string categoryName) => new TestLogger(categoryName, _entries);

    public void Clear()
    {
        while (_entries.TryDequeue(out _))
        {
        }
    }

    public string Snapshot() => string.Join(Environment.NewLine, _entries);

    public void Dispose()
    {
    }

    private sealed class TestLogger(
        string category,
        System.Collections.Concurrent.ConcurrentQueue<string> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            entries.Enqueue(
                $"{logLevel}|{category}|{formatter(state, exception)}|{exception?.GetType().Name}");
    }
}
