using Assistant.Abstractions.Proactive;
using Assistant.Persistence.Mcp;
using Microsoft.Extensions.DependencyInjection;

namespace Assistant.Persistence.Proactive;

public static class ProactivePersistenceExtensions
{
    public static IServiceCollection AddProactiveStores(this IServiceCollection services)
    {
        services.AddScoped<IGoalStore, GoalStore>();
        services.AddScoped<IPhraseShortcutStore, PhraseShortcutStore>();
        services.AddScoped<IAutomationStore, AutomationStore>();
        services.AddScoped<IBriefingStore, BriefingStore>();
        services.AddScoped<ISuggestionAuditStore, SuggestionAuditStore>();
        services.AddScoped<IProposalStore, ProposalStore>();
        services.AddScoped<IDiaryStore, DiaryStore>();
        services.AddScoped<IHeartbeatChecklistStore, HeartbeatChecklistStore>();
        services.AddScoped<IStandingOrderStore, StandingOrderStore>();
        services.AddScoped<IConditionalIntentStore, ConditionalIntentStore>();
        services.AddScoped<IMcpOAuthTokenStore, McpOAuthCredentialStore>();
        services.AddScoped<BriefingSeed>();
        return services;
    }
}
