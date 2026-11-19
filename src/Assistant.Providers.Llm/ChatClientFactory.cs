using System.ClientModel;
using Assistant.Providers.Llm.Configuration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenAI;

namespace Assistant.Providers.Llm;

internal static class ChatClientFactory
{
    public static IChatClient Create(ModelProfileOptions profile, ILoggerFactory loggers)
    {
        var options = new OpenAIClientOptions { Endpoint = new Uri(profile.Endpoint) };

        IChatClient client = new OpenAIClient(new ApiKeyCredential(profile.ApiKey), options)
            .GetChatClient(profile.Model)
            .AsIChatClient();

        return client.AsBuilder()
            // First registered sits outermost. Accounting is not here: it is added
            // per turn and outside everything else (D5, D6).
            .UseOpenTelemetry(loggers)
            .UseLogging(loggers)
            .ConfigureOptions(o => ApplyProfileOptions(o, profile))
            .Build();
    }

    /// <summary>
    /// Fills model/temperature/max tokens and, for openrouter, the provider extras object.
    /// Extracted so unit tests can assert serialization without a live HTTP client.
    /// </summary>
    internal static void ApplyProfileOptions(ChatOptions options, ModelProfileOptions profile)
    {
        options.ModelId ??= profile.Model;
        options.Temperature ??= profile.Temperature;
        options.MaxOutputTokens ??= profile.MaxOutputTokens;

        if (!profile.Provider.Equals("openrouter", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        options.AdditionalProperties ??= [];
        options.AdditionalProperties["provider"] = BuildOpenRouterProvider(profile.OpenRouter);
    }

    internal static Dictionary<string, object?> BuildOpenRouterProvider(OpenRouterProviderOptions? prefs)
    {
        var provider = new Dictionary<string, object?>();

        if (!string.IsNullOrWhiteSpace(prefs?.Sort))
        {
            provider["sort"] = prefs.Sort;
        }

        if (prefs?.Only is { Count: > 0 } only)
        {
            provider["only"] = only.ToArray();
        }

        if (prefs?.Ignore is { Count: > 0 } ignore)
        {
            provider["ignore"] = ignore.ToArray();
        }

        if (prefs?.AllowFallbacks is { } allowFallbacks)
        {
            provider["allow_fallbacks"] = allowFallbacks;
        }

        if (prefs?.Zdr is true)
        {
            provider["zdr"] = true;
        }

        if (!string.IsNullOrWhiteSpace(prefs?.DataCollection))
        {
            provider["data_collection"] = prefs.DataCollection;
        }

        return provider;
    }
}
