using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Json;
using Serilog.Sinks.OpenTelemetry;

namespace Assistant.Api.Observability;

internal static class SerilogRegistration
{
    public static void ConfigureSerilog(this WebApplicationBuilder builder)
    {
        Log.Logger = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .WriteTo.Console(new JsonFormatter())
            .CreateBootstrapLogger();

        builder.Host.UseSerilog((context, services, configuration) =>
        {
            configuration
                .ReadFrom.Configuration(context.Configuration)
                .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
                .MinimumLevel.Override("System.Net.Http.HttpClient.telegram", LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .WriteTo.Console(new JsonFormatter());

            foreach (var sink in services.GetServices<ILogEventSink>())
            {
                configuration.WriteTo.Sink(sink);
            }

            var endpoint = context.Configuration["OpenTelemetry:OtlpEndpoint"];
            if (!string.IsNullOrWhiteSpace(endpoint))
            {
                configuration.WriteTo.OpenTelemetry(options =>
                {
                    options.Endpoint = endpoint;
                    options.ResourceAttributes = new Dictionary<string, object>
                    {
                        ["service.name"] = "assistant-api",
                    };
                });
            }
        });
    }
}
