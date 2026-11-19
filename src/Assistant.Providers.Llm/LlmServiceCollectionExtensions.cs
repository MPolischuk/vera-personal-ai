using Assistant.Abstractions;
using Assistant.Abstractions.Tasks;
using Assistant.Core.Events;
using Assistant.Core.Memory;
using Assistant.Core.Models;
using Assistant.Providers.Llm.Configuration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Assistant.Providers.Llm;

public static class LlmServiceCollectionExtensions
{
    public static IServiceCollection AddAssistantLlm(this IServiceCollection services)
    {
        services.TryAddSingleton<IProviderFaultClassifier, OpenRouterBudgetFault>();
        services.TryAddSingleton<IModelCostQuote, ModelCostQuote>();
        services.TryAddSingleton<ChatClientCache>();
        services.TryAddSingleton<IModelRouter, ModelRouter>();
        services.AddSingleton<IValidateOptions<ModelsOptions>, ModelOptionsValidation>();
        services.TryAddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(provider =>
        {
            var profile = provider.GetRequiredService<IOptions<ModelsOptions>>().Value.Embedding;
            var inner = EmbeddingGeneratorFactory.Create(profile);
            return new UsageAccountingEmbeddingGenerator(
                inner,
                profile,
                provider.GetRequiredService<IOptions<CostOptions>>().Value,
                provider.GetRequiredService<IEventBus>(),
                provider.GetRequiredService<ITokenEstimator>(),
                provider.GetRequiredService<TimeProvider>(),
                provider.GetRequiredService<ILoggerFactory>()
                    .CreateLogger<UsageAccountingEmbeddingGenerator>());
        });
        services.TryAddSingleton<IMemoryEmbedder>(provider =>
        {
            var profile = provider.GetRequiredService<IOptions<ModelsOptions>>().Value.Embedding;
            return new DelegatingEmbeddingGenerator(
                provider.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>(),
                profile.QueryPrefix,
                profile.DocumentPrefix);
        });

        return services;
    }
}
