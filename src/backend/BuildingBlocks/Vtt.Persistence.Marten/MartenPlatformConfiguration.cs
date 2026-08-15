using JasperFx;
using JasperFx.Events;
using Marten;
using Microsoft.Extensions.DependencyInjection;

namespace Vtt.Persistence.Marten;

public sealed record MartenPlatformOptions(
    string ConnectionString,
    string SchemaName,
    bool ApplySchemaChanges = false);

public static class MartenPlatformConfiguration
{
    public static IServiceCollection AddVttMartenPersistence(
        this IServiceCollection services,
        MartenPlatformOptions platformOptions,
        Action<StoreOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(platformOptions);

        if (string.IsNullOrWhiteSpace(platformOptions.ConnectionString))
        {
            throw new ArgumentException(
                "A service-owned PostgreSQL connection string is required.",
                nameof(platformOptions));
        }

        if (string.IsNullOrWhiteSpace(platformOptions.SchemaName))
        {
            throw new ArgumentException("A service-owned schema is required.", nameof(platformOptions));
        }

        services.AddMarten(options =>
            {
                options.Connection(platformOptions.ConnectionString);
                options.DisableNpgsqlLogging = true;
                options.Logger(RedactedMartenLogger.Instance);
                options.DatabaseSchemaName = platformOptions.SchemaName;
                options.Events.DatabaseSchemaName = platformOptions.SchemaName;
                options.Events.StreamIdentity = StreamIdentity.AsGuid;
                options.Events.AppendMode = EventAppendMode.QuickWithServerTimestamps;
                options.AutoCreateSchemaObjects = platformOptions.ApplySchemaChanges
                    ? AutoCreate.CreateOrUpdate
                    : AutoCreate.None;

                options.Schema.For<OutboxMessage>().UseOptimisticConcurrency(true);
                options.Schema.For<InboxRecord>();
                options.Schema.For<IdempotencyRecord>();
                options.Schema.For<QuarantinedMessage>().UseOptimisticConcurrency(true);
                options.Schema.For<ProjectionCheckpoint>().UseOptimisticConcurrency(true);

                configure?.Invoke(options);
            })
            .UseLightweightSessions();

        return services;
    }
}
