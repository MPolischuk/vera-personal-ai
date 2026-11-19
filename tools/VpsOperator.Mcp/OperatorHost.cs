using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace VpsOperator.Mcp;

public static class OperatorHost
{
    public static IHost Create(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.Configure<ConsoleLifetimeOptions>(options => options.SuppressStatusMessages = true);

        builder.Services.AddSingleton<IRemoteShell, SshRemoteShell>();
        builder.Services.AddSingleton<IVpsEndpoint, SshConfigEndpoint>();
        builder.Services.AddSingleton<ITcpProbe, TcpProbe>();
        builder.Services.AddSingleton<OperatorTools>();
        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithTools<OperatorTools>();

        return builder.Build();
    }
}
