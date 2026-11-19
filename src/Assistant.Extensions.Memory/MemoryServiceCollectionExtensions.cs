using Assistant.Abstractions.Proactive;
using Assistant.Core;
using Assistant.Core.Events;
using Assistant.Extensions.Memory.Conditions;
using Assistant.Extensions.Memory.Contributors;
using Assistant.Extensions.Memory.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace Assistant.Extensions.Memory;

public static class MemoryServiceCollectionExtensions
{
    public static IServiceCollection AddAssistantMemory(this IServiceCollection services)
    {
        services.AddSingleton<MemoryPurgeService>();
        services.AddHostedService(sp => sp.GetRequiredService<MemoryPurgeService>());
        services.AddScoped<MemoryRecorder>();
        services.AddSingleton<MemoryConsolidationSchedule>();
        services.AddScoped<MemoryConsolidator>();
        services.AddDomainEventHandler<MemoryForgotten, InferredEvidenceForgottenHandler>();
        services.AddDomainEventHandler<MemoryStored, MemoryStoredConsolidationHandler>();
        services.AddAssistantTool<RememberTool>();
        services.AddAssistantTool<RecallTool>();
        services.AddAssistantTool<SearchMemoryTool>();
        services.AddAssistantTool<ForgetTool>();
        services.AddAssistantTool<GetPreferencesTool>();
        services.AddAssistantTool<UpdatePreferenceTool>();
        services.AddContextContributor<UserPreferencesContributor>();
        services.AddContextContributor<MemoryPolicyContributor>();
        services.AddContextContributor<PinnedMemoryContributor>();
        services.AddContextContributor<RelevantMemoryContributor>();
        services.AddContextContributor<LocalConditionsContributor>();
        services.AddHttpClient<ICurrentConditionsSource, OpenMeteoCurrentConditions>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(4);
        });
        services.AddScoped<IAmbientSource, BriefingWeatherSource>();
        return services;
    }
}
