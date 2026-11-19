using Assistant.Abstractions.Channels;
using Assistant.Channels.Telegram.Api;
using Assistant.Channels.Telegram.Inbound;
using Assistant.Channels.Telegram.Outbound;
using Assistant.Channels.Telegram.Polling;
using Assistant.Channels.Telegram.Webhook;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Timeout;

namespace Assistant.Channels.Telegram;

public static class TelegramServiceCollectionExtensions
{
    /// <summary>
    /// Binds and validates <see cref="TelegramOptions"/>. Registers the Bot API client only when
    /// <c>Telegram:Enabled</c> is true.
    /// </summary>
    public static IServiceCollection AddTelegramChannel(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSingleton<IValidateOptions<TelegramOptions>>(new TelegramOptionsValidator(configuration));
        services.AddOptions<TelegramOptions>()
            .Bind(configuration.GetSection(TelegramOptions.SectionName))
            .ValidateOnStart();

        if (!IsEnabled(configuration))
        {
            return services;
        }

        services.TryAddSingleton(TimeProvider.System);
        services.AddTransient<TelegramTimeoutHandler>();
        services.AddTransient<RedactingTelegramHandler>();

        // Registration order is outer to inner: resilience (retry), per-call timeout, token insertion.
        // The token is not in BaseAddress. AddStandardResilienceHandler returns a different builder, so the
        // later handlers stay on the original IHttpClientBuilder.
        var http = services.AddHttpClient(TelegramBotClient.HttpClientName, client =>
            {
                client.BaseAddress = new Uri("https://api.telegram.org/");
                client.Timeout = Timeout.InfiniteTimeSpan;
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
        http.AddStandardResilienceHandler(ConfigureResilience);
        http.AddHttpMessageHandler<TelegramTimeoutHandler>();
        http.AddHttpMessageHandler<RedactingTelegramHandler>();
        http.RemoveAllLoggers();

        services.AddSingleton<ITelegramBotClient, TelegramBotClient>();
        services.AddSingleton<TelegramChannelGate>();
        services.AddSingleton(new TelegramSpeechAvailability(IsSpeechConfigured(configuration)));
        services.AddSingleton<TelegramConfirmationMessages>();
        services.AddSingleton<ActiveDraftRegistry>();
        services.AddScoped<TelegramStopHandler>();
        services.AddSingleton<TelegramTurnSinkFactory>();
        services.AddSingleton<IChannelTurnSinkFactory>(sp => sp.GetRequiredService<TelegramTurnSinkFactory>());
        services.AddScoped<TelegramCallbackHandler>();
        services.AddScoped<TelegramInboundChannel>();
        services.AddScoped<IInboundChannel>(sp => sp.GetRequiredService<TelegramInboundChannel>());
        services.AddScoped<TelegramOutboundChannel>();
        services.AddScoped<IOutboundChannel>(sp => sp.GetRequiredService<TelegramOutboundChannel>());
        services.RemoveAll<IInboxItemHandler>();
        services.AddScoped<IInboxItemHandler, TelegramInboxItemHandler>();
        if (IsPolling(configuration))
        {
            services.AddHostedService<TelegramPollingService>();
        }
        else
        {
            services.AddHostedService<TelegramWebhookReconciler>();
        }

        return services;
    }

    private static void ConfigureResilience(HttpStandardResilienceOptions options)
    {
        // One attempt must cover getUpdates (40s). Shorter calls cancel sooner in TelegramTimeoutHandler.
        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(45);
        options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(3);
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(90);
        options.Retry.MaxRetryAttempts = 2;
        options.Retry.BackoffType = DelayBackoffType.Exponential;
        options.Retry.UseJitter = true;
        options.Retry.Delay = TimeSpan.FromMilliseconds(200);
        options.Retry.ShouldHandle = static args =>
        {
            if (args.Outcome.Exception is OperationCanceledException)
            {
                return ValueTask.FromResult(false);
            }

            if (args.Outcome.Exception is HttpRequestException or TimeoutException or TimeoutRejectedException)
            {
                return ValueTask.FromResult(true);
            }

            if (args.Outcome.Result is HttpResponseMessage response)
            {
                return ValueTask.FromResult((int)response.StatusCode >= 500);
            }

            return ValueTask.FromResult(false);
        };
    }

    private static bool IsEnabled(IConfiguration configuration)
    {
        var raw = configuration[$"{TelegramOptions.SectionName}:Enabled"];
        return bool.TryParse(raw, out var enabled) && enabled;
    }

    private static bool IsSpeechConfigured(IConfiguration configuration)
        => !string.IsNullOrWhiteSpace(configuration["Assistant:Speech:Stt:Provider"]);

    private static bool IsPolling(IConfiguration configuration)
    {
        var raw = configuration[$"{TelegramOptions.SectionName}:Mode"];
        return Enum.TryParse<TelegramReceiveMode>(raw, ignoreCase: true, out var mode)
            && mode == TelegramReceiveMode.Polling;
    }
}
