using Assistant.Abstractions.Channels;
using Assistant.Abstractions.Notifications;
using Assistant.Abstractions.Push;
using Assistant.Abstractions.Scheduling;
using Assistant.Abstractions.Webhooks;
using Assistant.Core.Audit;
using Assistant.Core.Confirmations;
using Assistant.Core.Conversations;
using Assistant.Core.Memory;
using Assistant.Core.Personality;
using Assistant.Core.Notes;
using Assistant.Core.Research;
using Assistant.Core.Skills;
using Assistant.Core.Usage;
using Assistant.Persistence.Audit;
using Assistant.Persistence.Auth;
using Assistant.Persistence.Channels;
using Assistant.Persistence.Confirmations;
using Assistant.Persistence.Conversations;
using Assistant.Persistence.Memory;
using Assistant.Persistence.Notifications;
using Assistant.Persistence.Personality;
using Assistant.Abstractions.Reminders;
using Assistant.Abstractions.Research;
using Assistant.Persistence.Push;
using Assistant.Persistence.Reminders;
using Assistant.Persistence.Notes;
using Assistant.Persistence.Research;
using Assistant.Abstractions.Tasks;
using Assistant.Persistence.Scheduling;
using Assistant.Persistence.Tasks;
using Assistant.Persistence.Skills;
using Assistant.Persistence.Usage;
using Assistant.Persistence.Webhooks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pgvector.EntityFrameworkCore;

namespace Assistant.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddAssistantPersistence(
        this IServiceCollection services, string connectionString)
    {
        services.TryAddSingleton(TimeProvider.System);

        services.AddDbContext<AssistantDbContext>((provider, options) =>
        {
            ConfigureContext(options, connectionString);
            options.AddInterceptors(new TimestampInterceptor(provider.GetRequiredService<TimeProvider>()));
        });

        services.AddScoped<IConversationStore, ConversationStore>();
        services.AddScoped<IPendingConfirmationStore, PendingConfirmationStore>();
        services.AddScoped<IPersonalityStore, PersonalityStore>();
        services.AddScoped<IUsageStore, UsageStore>();
        services.AddScoped<IAuditLogStore, AuditLogStore>();
        services.AddScoped<IMemoryStore, MemoryStore>();
        services.AddScoped<MemoryReembedStore>();
        services.AddScoped<IPreferenceStore, PreferenceStore>();
        services.AddScoped<ISkillProposalStore, SkillProposalStore>();
        services.AddSingleton<Argon2idPasswordHasher>();
        services.AddScoped<IUserPasswordStore, UserPasswordStore>();
        services.AddScoped<IDeviceStore, DeviceStore>();

        services.AddScoped<IChannelPeerDirectory, ChannelPeerStore>();
        services.AddScoped<IChannelPairingStore, ChannelPairingStore>();
        services.AddScoped<IChannelInboxStore, ChannelInboxStore>();
        services.AddScoped<INotificationStore, NotificationStore>();
        services.AddScoped<INotificationDeliveryStore, NotificationDeliveryStore>();
        services.AddScoped<IPushSubscriptionReader, PushSubscriptionStore>();
        services.AddScoped<IJobStore, JobStore>();
        services.AddScoped<IAgentTaskStore, AgentTaskStore>();
        services.AddScoped<ReminderStore>();
        services.AddScoped<IReminderStore>(provider => provider.GetRequiredService<ReminderStore>());
        services.AddScoped<IReminderQueryService>(provider => provider.GetRequiredService<ReminderStore>());
        services.AddScoped<IReminderFiringStore, ReminderFiringStore>();
        services.AddScoped<IInboundWebhookDeliveryStore, InboundWebhookDeliveryStore>();
        services.AddScoped<IResearchSearchLog, ResearchSearchLog>();
        services.AddScoped<IResearchReadStore, ResearchReadStore>();
        services.AddScoped<INoteStore, NoteStore>();

        return services;
    }

    /// <summary>
    /// Single provider configuration path. Used by composition and the design-time factory:
    /// if they diverge, migrations are generated against one model and the app runs against another.
    /// </summary>
    internal static void ConfigureContext(DbContextOptionsBuilder options, string connectionString)
        => options
            .UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsHistoryTable("__ef_migrations_history");
                npgsql.UseVector();
            })
            .UseSnakeCaseNamingConvention();
}
