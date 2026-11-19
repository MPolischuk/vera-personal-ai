using Assistant.Abstractions.Proactive;
using Assistant.Abstractions.Scheduling;
using Assistant.Abstractions.Webhooks;
using Assistant.Core;
using Assistant.Extensions.Proactive.Sources;
using Assistant.Extensions.Proactive.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace Assistant.Extensions.Proactive;

public static class ProactiveServiceCollectionExtensions
{
    public static IServiceCollection AddAssistantProactive(this IServiceCollection services)
    {
        services.AddOptions<BriefingOptions>();
        services.AddOptions<ProactiveOptions>()
            .BindConfiguration(ProactiveOptions.SectionName);
        services.AddScoped<IAmbientSource, GoalAmbientSource>();
        services.AddScoped<IAmbientSource, ReminderAmbientSource>();
        services.AddScoped<IAmbientSource, CalendarAmbientSource>();
        services.AddScoped<IAmbientSource, StandingOrderSource>();
        services.AddScoped<IAmbientSource, RecentActivitySource>();
        services.AddScoped<IAmbientSource, PresenceSource>();
        services.AddSingleton<IBriefingSkillSource, BriefingSkillFile>();
        services.AddScoped<BriefingShortcut>();
        services.AddScoped<IJobHandler, BriefingGenerateHandler>();
        services.AddScoped<IJobHandler, AutomationRunHandler>();
        services.AddScoped<ReadOnlyToolLoop>();
        services.AddScoped<HeartbeatRunner>();
        services.AddScoped<IJobHandler, HeartbeatJob>();
        services.AddScoped<IJobHandler, ContradictionReviewJob>();
        services.AddScoped<ProposalApproval>();
        services.AddAssistantTool<SetGoalTool>();
        services.AddAssistantTool<CompleteGoalTool>();
        services.AddAssistantTool<ListGoalsTool>();
        services.AddAssistantTool<SetPhraseShortcutTool>();
        services.AddAssistantTool<ListAutomationsTool>();
        services.AddAssistantTool<CreateAutomationTool>();
        services.AddAssistantTool<PauseAutomationTool>();
        services.AddAssistantTool<DeleteAutomationTool>();
        services.AddAssistantTool<SetHeartbeatChecklistTool>();
        services.AddAssistantTool<ListHeartbeatChecklistTool>();
        services.AddAssistantTool<SetStandingOrderTool>();
        services.AddAssistantTool<PauseStandingOrderTool>();
        services.AddAssistantTool<ListStandingOrdersTool>();
        services.AddAssistantTool<SetConditionalIntentTool>();
        services.AddAssistantTool<PauseConditionalIntentTool>();
        services.AddAssistantTool<ListConditionalIntentsTool>();
        services.AddDomainEventHandler<InboundFactReceived, InboundFactHandler>();
        return services;
    }
}
