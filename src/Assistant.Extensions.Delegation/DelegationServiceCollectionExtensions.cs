using Assistant.Abstractions.Notifications;
using Assistant.Abstractions.Tasks;
using Assistant.Core;
using Assistant.Extensions.Delegation.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Assistant.Extensions.Delegation;

public static class DelegationServiceCollectionExtensions
{
    /// <summary>
    /// Registers the task shell, <c>list_tasks</c>, <c>cancel_task</c> and <c>delegate_work</c>.
    /// The host registers <c>IAgentTaskStore</c>.
    /// </summary>
    public static IServiceCollection AddAssistantDelegation(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<DelegationOptions>()
            .Bind(configuration.GetSection(DelegationOptions.SectionName));
        services.AddSingleton<AgentTaskRuns>();
        services.AddAssistantTool<ListTasksTool>();
        services.AddAssistantTool<CancelTaskTool>();
        services.AddAssistantTool<DelegateWorkTool>();
        services.AddScoped<IAgentWorker, DraftWorker>();
        services.AddScoped<TaskResultSpeaker>();
        services.AddHostedService<TaskWorker>();
        return services;
    }
}

/// <summary>The same cancel path <c>cancel_task</c> uses. Signaling does not wait.</summary>
public static class AgentTaskCancellation
{
    public static ValueTask<bool> TryCancelAsync(
        IServiceProvider services,
        Guid taskId,
        Guid userId,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(services);
        return TryCancelAsync(
            services.GetRequiredService<IAgentTaskStore>(),
            services.GetRequiredService<INotificationPublisher>(),
            services.GetRequiredService<AgentTaskRuns>(),
            services.GetRequiredService<TimeProvider>(),
            taskId,
            userId,
            ct);
    }

    internal static async ValueTask<bool> TryCancelAsync(
        IAgentTaskStore store,
        INotificationPublisher publisher,
        AgentTaskRuns runs,
        TimeProvider clock,
        Guid taskId,
        Guid userId,
        CancellationToken ct)
    {
        var notice = await store.CancelAsync(taskId, userId, clock.GetUtcNow(), ct);
        if (notice is null)
        {
            return false;
        }

        runs.Signal(taskId);
        await AgentTaskNoticePublisher.PublishArmedAsync(
            store,
            publisher,
            clock.GetUtcNow(),
            CancellationToken.None);
        return true;
    }
}
