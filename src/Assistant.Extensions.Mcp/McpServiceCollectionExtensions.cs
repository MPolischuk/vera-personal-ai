using Assistant.Abstractions.Proactive;
using Assistant.Abstractions.Skills;
using Assistant.Abstractions.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Assistant.Extensions.Mcp;

public static class McpServiceCollectionExtensions
{
    public static IServiceCollection AddAssistantMcp(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddOptions<McpOptions>()
            .Bind(configuration.GetSection(McpOptions.SectionName));
        return RegisterCore(services);
    }

    public static IServiceCollection AddAssistantMcp(
        this IServiceCollection services,
        Action<McpOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        services.AddOptions<McpOptions>().Configure(configure);
        return RegisterCore(services);
    }

    private static IServiceCollection RegisterCore(IServiceCollection services)
    {
        services.TryAddSingleton<IMcpToolClassification, ManifestToolClassification>();
        services.AddSingleton<McpTaskPoller>();
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<McpTaskPoller>());
        services.AddSingleton<IMcpAccessTokenRefresher, McpAccessTokenRefresher>();
        services.AddSingleton<McpSessionHost>();
        services.AddSingleton<IToolSource>(sp => sp.GetRequiredService<McpSessionHost>());
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<McpSessionHost>());
        services.AddSingleton<IExternalSkillSource>(sp => sp.GetRequiredService<McpSessionHost>());
        services.AddSingleton<IConnectedMcpServers>(sp => sp.GetRequiredService<McpSessionHost>());
        services.AddSingleton<IExtensionManager, ExtensionManager>();
        return services;
    }
}
