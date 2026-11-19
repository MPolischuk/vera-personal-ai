namespace Assistant.Mcp.Search;

public static class SearchHost
{
    public static WebApplication Create(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        configure?.Invoke(builder);

        var origin = SearchSettings.Origin(builder.Configuration);
        builder.Services.AddHttpClient(SearchTools.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(SearchSettings.TimeoutSeconds);
            if (origin is not null)
            {
                client.BaseAddress = origin;
            }
        }).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
        }).RemoveAllLoggers();

        builder.Services
            .AddMcpServer()
            .WithHttpTransport()
            .WithTools<SearchTools>();

        var app = builder.Build();
        app.MapGet("/health", () => Results.Ok());
        app.MapMcp();
        return app;
    }
}
