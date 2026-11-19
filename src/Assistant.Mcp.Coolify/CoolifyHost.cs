using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Extensions.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Assistant.Mcp.Coolify;

public static class CoolifyHost
{
    public static WebApplication Create(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        configure?.Invoke(builder);

        using var startupLogs = LoggerFactory.Create(logging => logging.AddConsole());
        var allowlist = CoolifyAllowlist.Parse(
            builder.Configuration["COOLIFY_ALLOWLIST"],
            startupLogs.CreateLogger("Assistant.Mcp.Coolify.Allowlist"));
        var settings = CoolifySettings.From(builder.Configuration);

        builder.Services.AddSingleton(allowlist);
        builder.Services.AddSingleton(settings);
        builder.Services.AddHttpClient<ICoolifyApi, HttpCoolifyApi>((_, client) =>
        {
            client.Timeout = TimeSpan.FromSeconds(CoolifySettings.HttpTimeoutSeconds);
            if (settings.Origin is not null)
            {
                client.BaseAddress = settings.Origin;
            }

            if (settings.Token is not null)
            {
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", settings.Token);
            }
        });

        builder.Services
            .AddMcpServer()
            .WithHttpTransport()
            .WithTools(CoolifyToolCatalog.Create(allowlist))
            .WithTasks(new InMemoryMcpTaskStore(), options =>
            {
                options.ExecutionModeSelector = SelectTaskMode;
            });

        var app = builder.Build();
        app.MapGet("/health", () => Results.Ok());
        app.MapMcp();
        return app;
    }

    private static McpTaskExecutionMode SelectTaskMode(RequestContext<CallToolRequestParams> context)
    {
        if (context.Params?.Name is not "control"
            || context.Params.Arguments is not { } arguments
            || !arguments.TryGetValue("action", out var action)
            || action.ValueKind != JsonValueKind.String
            || !string.Equals(action.GetString(), "deploy", StringComparison.OrdinalIgnoreCase))
        {
            return McpTaskExecutionMode.Synchronous;
        }

        return McpTaskExecutionMode.Optional;
    }
}
