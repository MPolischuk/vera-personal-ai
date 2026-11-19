using Assistant.Abstractions.Channels;
using Assistant.Abstractions.Notifications;
using Assistant.Abstractions.Scheduling;
using Assistant.Channels.Delivery;
using Assistant.Channels.Inbox;
using Assistant.Channels.Notifications;
using Assistant.Channels.Pairing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Assistant.Channels;

public static class ChannelsServiceCollectionExtensions
{
    public static IServiceCollection AddChannels(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IValidateOptions<DeliveryOptions>, DeliveryOptionsValidator>();
        services.AddOptions<DeliveryOptions>()
            .Bind(configuration.GetSection(DeliveryOptions.SectionName))
            .ValidateOnStart();

        services.AddOptions<InboxOptions>()
            .Bind(configuration.GetSection(InboxOptions.SectionName));
        services.AddOptions<PairingOptions>()
            .Bind(configuration.GetSection(PairingOptions.SectionName));
        services.TryAddSingleton<PairingCodeHold>();
        services.TryAddScoped<IChannelAuditSink, NullChannelAuditSink>();
        services.AddScoped<PairingService>();
        services.AddScoped<IPairingService>(sp => sp.GetRequiredService<PairingService>());
        services.AddScoped<IChannelPairingAdmin>(sp => sp.GetRequiredService<PairingService>());
        services.AddScoped<PairingInboxStep>();
        services.AddScoped<IJobHandler, PairingExpireHandler>();
        services.AddSingleton<IChannelIngressTelemetry, ChannelIngressTelemetry>();
        services.AddSingleton<InboxProcessor>();
        services.AddHostedService(sp => sp.GetRequiredService<InboxProcessor>());
        services.AddHostedService<InboxRecoveryService>();
        services.AddSingleton<IInboxItemHandler, UnhandledInboxItemHandler>();
        services.AddScoped<IInboxAcceptor, InboxAcceptor>();
        services.AddScoped<IJobHandler, InboxPurgeHandler>();

        services.AddScoped<QuietHoursPolicy>();
        services.AddScoped<DeliveryRouter>();
        services.AddScoped<INotificationPublisher, NotificationPublisher>();
        services.AddOptions<NotificationActionOptions>()
            .Bind(configuration.GetSection(NotificationActionOptions.SectionName));
        services.AddScoped<INotificationActionService, NotificationActionService>();
        services.AddScoped<IJobHandler, NotificationDispatchHandler>();
        services.AddScoped<IChannelRegistry, ChannelRegistry>();
        return services;
    }
}
