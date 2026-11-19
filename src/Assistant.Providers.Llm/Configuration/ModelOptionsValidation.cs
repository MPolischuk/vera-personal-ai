using Assistant.Core.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Assistant.Providers.Llm.Configuration;

internal sealed class ModelOptionsValidation(
    IOptions<CostOptions> costs,
    ILogger<ModelOptionsValidation>? logger = null) : IValidateOptions<ModelsOptions>
{
    public ValidateOptionsResult Validate(string? name, ModelsOptions options)
    {
        WarnIfConversationLacksZdr(options);

        var problems = options.DeclaredProfiles()
            .Where(profile => !costs.Value.Prices.ContainsKey(profile.Model))
            .Select(MissingPrice)
            .ToList();

        var embedding = options.Embedding;
        if (string.IsNullOrWhiteSpace(embedding.Provider) || string.IsNullOrWhiteSpace(embedding.Model))
        {
            problems.Add(
                "Assistant:Models:Embedding is required (Provider, Model, Dimensions).");
        }
        else if (IsLocalProvider(embedding.Provider))
        {
            if (string.IsNullOrWhiteSpace(embedding.ModelPath))
            {
                problems.Add(
                    "Assistant:Models:Embedding:ModelPath is required when Provider is Local. " +
                    "Point it at the fp32 directory that contains model.onnx and model.onnx_data.");
            }

            RequireSchemaDimensions(problems, embedding);
            RequirePrice(problems, embedding.Model);
        }
        else if (string.IsNullOrWhiteSpace(embedding.Endpoint)
            || string.IsNullOrWhiteSpace(embedding.ApiKey)
            || !IsAbsoluteHttpUrl(embedding.Endpoint))
        {
            problems.Add(
                "Assistant:Models:Embedding is required (Provider, Endpoint, Model, ApiKey, Dimensions).");
        }
        else
        {
            if (IsTestProvider(embedding.Provider))
            {
                if (embedding.Dimensions is < DeterministicEmbeddingGenerator.MinDimensions
                    or > DeterministicEmbeddingGenerator.MaxDimensions)
                {
                    problems.Add(
                        "Assistant:Models:Embedding:Dimensions must be an integer from " +
                        $"{DeterministicEmbeddingGenerator.MinDimensions} to " +
                        $"{DeterministicEmbeddingGenerator.MaxDimensions} when Provider is test.");
                }
            }
            else
            {
                RequireSchemaDimensions(problems, embedding);
            }

            RequirePrice(problems, embedding.Model);
        }

        return problems.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(problems);
    }

    private void WarnIfConversationLacksZdr(ModelsOptions options)
    {
        var conversation = options.Conversation;
        if (!conversation.Provider.Equals("openrouter", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (conversation.OpenRouter?.Zdr is true)
        {
            return;
        }

        if (logger is not null)
        {
            ModelOptionsLog.ConversationLacksZdr(logger, "Conversation");
        }
    }

    private static void RequireSchemaDimensions(List<string> problems, EmbeddingProfileOptions embedding)
    {
        if (embedding.Dimensions != MemorySchema.EmbeddingDimensions)
        {
            problems.Add(
                $"Assistant:Models:Embedding:Dimensions must be {MemorySchema.EmbeddingDimensions} " +
                "to match memories.embedding. Changing N requires a new migration.");
        }
    }

    private void RequirePrice(List<string> problems, string model)
    {
        if (!costs.Value.Prices.ContainsKey(model))
        {
            problems.Add(MissingPrice(model));
        }
    }

    private static bool IsAbsoluteHttpUrl(string value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri)
           && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static bool IsLocalProvider(string provider)
        => provider.Equals("Local", StringComparison.OrdinalIgnoreCase);

    private static bool IsTestProvider(string provider)
        => provider.Equals("test", StringComparison.OrdinalIgnoreCase);

    private static string MissingPrice(ModelProfileOptions profile) => MissingPrice(profile.Model);

    private static string MissingPrice(string model)
        => $"Model '{model}' has no price in Assistant:Costs:Prices. " +
           "If it is free, declare it with InputPerMillionUsd and OutputPerMillionUsd set to 0: " +
           "an explicit zero shows it was considered; absence says nothing.";
}

internal static partial class ModelOptionsLog
{
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Profile {Profile} uses OpenRouter without Zdr set to true. Memory text sent on this profile may be retained by the provider.")]
    public static partial void ConversationLacksZdr(ILogger logger, string profile);
}
