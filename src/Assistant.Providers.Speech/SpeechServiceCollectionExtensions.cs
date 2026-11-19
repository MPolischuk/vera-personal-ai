using Assistant.Core.Events;
using Assistant.Core.Speech;
using Assistant.Providers.Speech.Configuration;
using Assistant.Providers.Speech.Deepgram;
using Assistant.Providers.Speech.ElevenLabs;
using Assistant.Providers.Speech.OpenRouter;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Assistant.Providers.Speech;

public static class SpeechServiceCollectionExtensions
{
    /// <summary>
    /// Registers speech validation and, when configuration names a vendor, the cloud
    /// adapters wrapped in usage accounting. Does not bind options (Api does).
    /// Empty Provider skips registration for that side (D7).
    /// </summary>
    public static IServiceCollection AddAssistantSpeech(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IValidateOptions<SpeechOptions>, SpeechOptionsValidation>();
        services.TryAddSingleton(TimeProvider.System);

        var section = configuration.GetSection(SpeechOptions.SectionName);
        var sttProvider = section.GetSection("Stt")["Provider"];
        var ttsProvider = section.GetSection("Tts")["Provider"];

        if (string.Equals(sttProvider, SpeechOptionsValidation.DeepgramProvider, StringComparison.OrdinalIgnoreCase))
        {
            services.AddHttpClient(DeepgramSpeechToTextProvider.HttpClientName);
            services.AddSingleton<DeepgramSpeechToTextProvider>();
            services.AddSingleton<ISpeechToTextProvider>(sp =>
            {
                var options = sp.GetRequiredService<IOptions<SpeechOptions>>().Value.Stt;
                var inner = sp.GetRequiredService<DeepgramSpeechToTextProvider>();
                return WrapStt(sp, inner, options);
            });
        }
        else if (string.Equals(sttProvider, SpeechOptionsValidation.OpenRouterProvider, StringComparison.OrdinalIgnoreCase))
        {
            services.AddHttpClient(OpenRouterSpeechToTextProvider.HttpClientName);
            services.AddSingleton<OpenRouterSpeechToTextProvider>();
            services.AddSingleton<ISpeechToTextProvider>(sp =>
            {
                var options = sp.GetRequiredService<IOptions<SpeechOptions>>().Value.Stt;
                var inner = sp.GetRequiredService<OpenRouterSpeechToTextProvider>();
                return WrapStt(sp, inner, options);
            });
        }
        else if (string.Equals(
                     sttProvider,
                     SpeechOptionsValidation.ElevenLabsRealtimeProvider,
                     StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<ElevenLabsRealtimeSpeechToTextProvider>();
            services.AddSingleton<ISpeechToTextProvider>(sp =>
            {
                var options = sp.GetRequiredService<IOptions<SpeechOptions>>().Value.Stt;
                var inner = sp.GetRequiredService<ElevenLabsRealtimeSpeechToTextProvider>();
                return WrapStt(sp, inner, options);
            });
        }

        if (!string.IsNullOrWhiteSpace(ttsProvider))
        {
            services.AddHttpClient(ElevenLabsTextToSpeechProvider.HttpClientName, (sp, client) =>
            {
                var endpoint = sp.GetRequiredService<IOptions<SpeechOptions>>().Value.Tts.Endpoint;
                if (!string.IsNullOrWhiteSpace(endpoint))
                {
                    client.BaseAddress = new Uri(EnsureTrailingSlash(endpoint));
                }
            });
            services.AddSingleton<ElevenLabsTextToSpeechProvider>();
            services.AddSingleton<ITextToSpeechProvider>(sp =>
            {
                var options = sp.GetRequiredService<IOptions<SpeechOptions>>().Value.Tts;
                var inner = sp.GetRequiredService<ElevenLabsTextToSpeechProvider>();
                return new UsageAccountingTextToSpeechProvider(
                    inner,
                    options.Provider,
                    options.Model,
                    options.PricePerMillionCharactersUsd,
                    sp.GetRequiredService<IEventBus>(),
                    sp.GetRequiredService<TimeProvider>(),
                    sp.GetRequiredService<ILoggerFactory>()
                        .CreateLogger("Assistant.Providers.Speech.UsageAccounting.Tts"));
            });
        }

        return services;
    }

    private static UsageAccountingSpeechToTextProvider WrapStt(
        IServiceProvider services,
        ISpeechToTextProvider inner,
        SttOptions options)
    {
        return new UsageAccountingSpeechToTextProvider(
            inner,
            options.Provider,
            options.Model,
            options.PricePerMinuteUsd,
            services.GetRequiredService<IEventBus>(),
            services.GetRequiredService<TimeProvider>(),
            services.GetRequiredService<ILoggerFactory>()
                .CreateLogger("Assistant.Providers.Speech.UsageAccounting.Stt"));
    }

    private static string EnsureTrailingSlash(string endpoint)
        => endpoint.EndsWith('/') ? endpoint : endpoint + "/";
}
