using Assistant.Core.Context.Contributors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Assistant.Core.Conversations;

public static class ConversationServiceCollectionExtensions
{
    public static IServiceCollection AddAssistantConversation(this IServiceCollection services)
    {
        services.TryAddSingleton<ActiveTurns>();
        services.TryAddSingleton<IActiveTurns>(sp => sp.GetRequiredService<ActiveTurns>());
        services.AddScoped<IConversationOrchestrator, ConversationOrchestrator>();
        services.AddContextContributor<ConversationHistoryContributor>();

        return services;
    }
}
