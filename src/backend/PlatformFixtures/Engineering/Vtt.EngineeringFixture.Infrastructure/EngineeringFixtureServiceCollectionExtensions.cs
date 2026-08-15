using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Projections;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Vtt.Cqrs;
using Vtt.EngineeringFixture.Application;
using Vtt.EngineeringFixture.Domain;
using Vtt.Messaging.Nats;
using Vtt.Persistence.Marten;

namespace Vtt.EngineeringFixture.Infrastructure;

public static class EngineeringFixtureServiceCollectionExtensions
{
    private const string LocalConnectionString =
        "Host=127.0.0.1;Port=55432;Database=vtt_engineering;" +
        "Username=vtt_engineering;Password=local-only-service-db";

    public static IServiceCollection AddEngineeringFixture(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration["VTT_ENGINEERING_CONNECTION_STRING"]
            ?? configuration.GetConnectionString("Engineering")
            ?? LocalConnectionString;
        var applySchema = configuration.GetValue("VTT_APPLY_SCHEMA", false);

        services.TryAddSingleton(TimeProvider.System);
        services.AddVttMartenPersistence(
            new MartenPlatformOptions(connectionString, "engineering", applySchema),
            options =>
            {
                options.Events.AddEventType<ProbeIncremented>();
                options.Projections.Add<ProbeAggregateLiveProjection>(ProjectionLifecycle.Live);
                options.Schema.For<ProbeSnapshot>().UseOptimisticConcurrency(true);
                options.Schema.For<ProbeProjection>().UseOptimisticConcurrency(true);
                options.Schema.For<PlatformOperationRecord>().UseOptimisticConcurrency(true);
            });

        services.AddVttNatsMessaging(new NatsMessagingOptions
        {
            Url = configuration["NATS_URL"] ?? "nats://127.0.0.1:54222",
            ClientName = "engineering-fixture",
        });
        services.AddVttOutboxRelay(options =>
        {
            options.BatchSize = 50;
            options.MaxAttempts = 20;
            options.PollInterval = TimeSpan.FromMilliseconds(250);
            options.MaximumBackoff = TimeSpan.FromSeconds(30);
        });

        services.AddScoped<IProbeCommandStore, MartenProbeCommandStore>();
        services.AddScoped<IProbeProjectionReader, MartenProbeProjectionReader>();
        services.AddScoped<IProjectionOperations, MartenProjectionOperations>();
        services.AddScoped<IMessageOperations, MessageOperations>();
        services.AddSingleton<EngineeringSchemaMigrator>();

        services.AddScoped<ICommandHandler<IncrementProbeCommand, IncrementProbeResult>,
            IncrementProbeHandler>();
        services.AddScoped<ICommandValidator<IncrementProbeCommand>, IncrementProbeValidator>();
        services.AddScoped<CommandPipeline<IncrementProbeCommand, IncrementProbeResult>>();
        services.AddScoped<IQueryHandler<GetProbeProjectionQuery, ProbeProjectionResult?>,
            GetProbeProjectionHandler>();
        services.AddScoped<QueryPipeline<GetProbeProjectionQuery, ProbeProjectionResult?>>();

        services.AddSingleton<ProbeIntegrationEventProcessor>();
        services.AddHostedService<ProbeJetStreamConsumer>();
        services.AddHostedService<ProjectionRebuildWorker>();

        return services;
    }
}
