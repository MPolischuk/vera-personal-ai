using System.ClientModel;
using Assistant.Providers.Llm.Configuration;
using Microsoft.Extensions.AI;
using OpenAI;

namespace Assistant.Providers.Llm;

internal static class EmbeddingGeneratorFactory
{
    public static IEmbeddingGenerator<string, Embedding<float>> Create(EmbeddingProfileOptions profile)
    {
        if (profile.Provider.Equals("test", StringComparison.OrdinalIgnoreCase))
        {
            return new DeterministicEmbeddingGenerator(profile.Dimensions);
        }

        if (profile.Provider.Equals("Local", StringComparison.OrdinalIgnoreCase))
        {
            return new LocalEmbeddingGenerator(
                new HarrierFp32EmbeddingModel(profile.ModelPath),
                profile.Model,
                profile.Dimensions);
        }

        var options = new OpenAIClientOptions { Endpoint = new Uri(profile.Endpoint) };
        IEmbeddingGenerator<string, Embedding<float>> generator = new OpenAIClient(
                new ApiKeyCredential(profile.ApiKey), options)
            .GetEmbeddingClient(profile.Model)
            .AsIEmbeddingGenerator(profile.Dimensions);

        if (!profile.Provider.Equals("openrouter", StringComparison.OrdinalIgnoreCase))
        {
            return generator;
        }

        return generator.AsBuilder()
            .ConfigureOptions(embeddingOptions => ApplyOpenRouterOptions(embeddingOptions, profile))
            .Build();
    }

    /// <summary>
    /// For openrouter, copies the provider object onto the embedding options.
    /// Other providers, including test, leave the options unchanged.
    /// </summary>
    internal static void ApplyOpenRouterOptions(
        EmbeddingGenerationOptions options, EmbeddingProfileOptions profile)
    {
        if (!profile.Provider.Equals("openrouter", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        options.AdditionalProperties ??= [];
        options.AdditionalProperties["provider"] = ChatClientFactory.BuildOpenRouterProvider(profile.OpenRouter);
    }
}
