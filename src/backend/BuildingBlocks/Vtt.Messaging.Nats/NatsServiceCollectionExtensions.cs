using Microsoft.Extensions.DependencyInjection;
using NATS.Client.Core;
using NATS.Client.JetStream;
using Vtt.Messaging;

namespace Vtt.Messaging.Nats;

public static class NatsServiceCollectionExtensions
{
    public static IServiceCollection AddVttNatsMessaging(
        this IServiceCollection services,
        NatsMessagingOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        services.AddSingleton(options);
        services.AddSingleton<INatsConnection>(_ =>
            NatsConnectionFactory.Create(options, "shared"));
        services.AddSingleton<INatsJSContext>(provider =>
            new NatsJSContext(provider.GetRequiredService<INatsConnection>()));
        services.AddSingleton<IIntegrationEventPublisher, NatsIntegrationEventPublisher>();
        return services;
    }
}

public static class NatsConnectionFactory
{
    public static NatsConnection Create(NatsMessagingOptions options, string role) =>
        new(new NatsOpts
        {
            Url = options.Url,
            Name = $"{options.ClientName}-{role}",
            MaxPayloadHardCap = 1024 * 1024,
            DrainSubscriptionsOnDispose = true,
            RetryOnInitialConnect = true,
            ReconnectWaitMin = TimeSpan.FromMilliseconds(250),
            ReconnectWaitMax = TimeSpan.FromSeconds(2),
            MaxReconnectRetry = -1,
        });
}
