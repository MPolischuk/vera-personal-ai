using System.Diagnostics;
using Assistant.Channels;
using Assistant.Channels.Telegram.Api;
using Assistant.Extensions.Reminders;
using Assistant.Extensions.Skills;
using Assistant.Persistence;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Assistant.Api.Observability;

internal static class OpenTelemetryRegistration
{
    public static IServiceCollection AddAssistantOpenTelemetry(
        this IServiceCollection services, IConfiguration configuration)
    {
        var endpoint = configuration["OpenTelemetry:OtlpEndpoint"];
        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService("assistant-api"))
            .WithTracing(tracing =>
            {
                tracing
                    .AddSource("Assistant.Core")
                    .AddSource("Assistant.Api")
                    .AddSource("Assistant.Extensions.Memory")
                    .AddSource(ChannelsTelemetry.ActivitySourceName)
                    .AddSource(SkillTelemetry.ActivitySourceName)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation(options =>
                    {
                        options.FilterHttpRequestMessage = static _ => true;
                        options.EnrichWithHttpRequestMessage = static (activity, _) =>
                            TelegramPathRedaction.RedactActivity(activity);
                        options.EnrichWithHttpResponseMessage = static (activity, _) =>
                            TelegramPathRedaction.RedactActivity(activity);
                        options.EnrichWithException = static (activity, _) =>
                            TelegramPathRedaction.RedactActivity(activity);
                    })
                    .AddAssistantPersistenceInstrumentation();

                if (!string.IsNullOrWhiteSpace(endpoint))
                {
                    tracing.AddOtlpExporter(options => options.Endpoint = new Uri(endpoint));
                }
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddMeter("Assistant.Core")
                    .AddMeter("Assistant.Api")
                    .AddMeter("Assistant.Providers.Llm")
                    .AddMeter("Assistant.Providers.Speech")
                    .AddMeter("Assistant.Extensions.Memory")
                    .AddMeter("Assistant.Scheduling")
                    .AddMeter("Assistant.Channels")
                    .AddMeter(ReminderTelemetry.MeterName)
                    .AddAspNetCoreInstrumentation()
                    .AddRuntimeInstrumentation();

                if (!string.IsNullOrWhiteSpace(endpoint))
                {
                    metrics.AddOtlpExporter(options => options.Endpoint = new Uri(endpoint));
                }
            });

        RedactTelegramHttpActivities();
        return services;
    }

    /// <summary>
    /// The native <c>System.Net.Http</c> activity stores <c>url.full</c> with the bot token in the path.
    /// Enrichment above rewrites the instrumentation tags; this listener covers the same activity at stop,
    /// before the exporter reads it. Sampling stays <see cref="ActivitySamplingResult.None"/> so the listener
    /// does not create activities the tracer would have dropped.
    /// </summary>
    private static void RedactTelegramHttpActivities()
    {
        if (Interlocked.Exchange(ref ListenerRegistered, 1) != 0)
        {
            return;
        }

        ActivitySource.AddActivityListener(new ActivityListener
        {
            ShouldListenTo = static source => source.Name is "System.Net.Http" or "OpenTelemetry.Instrumentation.Http",
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.None,
            ActivityStopped = TelegramPathRedaction.RedactActivity,
        });
    }

    private static int ListenerRegistered;
}
