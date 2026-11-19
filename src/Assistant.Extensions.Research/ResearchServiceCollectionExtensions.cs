using Assistant.Abstractions.Research;
using Assistant.Core;
using Assistant.Core.Research;
using Assistant.Extensions.Research.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Assistant.Extensions.Research;

public static class ResearchServiceCollectionExtensions
{
    /// <summary>
    /// Binds <see cref="ResearchOptions"/>, registers <see cref="PageFetcher"/> with manual redirects,
    /// and registers <c>read_source</c>. The read budget is not the search budget.
    /// </summary>
    public static IServiceCollection AddAssistantResearch(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<ResearchOptions>()
            .Bind(configuration.GetSection(ResearchOptions.SectionName));

        services.AddHttpClient(PageFetcher.HttpClientName, (provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<ResearchOptions>>().Value;
            client.Timeout = options.PageTimeout;
        }).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            ConnectCallback = PageFetcher.ConnectAsync,
        });

        services.AddScoped(provider => new PageFetcher(
            provider.GetRequiredService<IHttpClientFactory>().CreateClient(PageFetcher.HttpClientName),
            provider.GetRequiredService<IOptions<ResearchOptions>>()));
        services.AddSingleton<IMeteredToolBudget, SearchBudget>();
        services.AddSingleton<ReadBudget>();
        services.AddSingleton<SourceSummarizer>();
        services.AddSingleton<IDocumentLibrary>(provider =>
        {
            var research = provider.GetRequiredService<IOptions<ResearchOptions>>().Value;
            return new DocumentLibrary(research.DocumentRoot, research.MaxDocumentBytes);
        });
        services.AddHttpClient(DocumentConverter.HttpClientName, (provider, client) =>
        {
            var research = provider.GetRequiredService<IOptions<ResearchOptions>>().Value;
            client.Timeout = research.ConversionTimeout;
            if (research.DoclingBaseUrl is { } baseUrl)
            {
                var absolute = baseUrl.AbsoluteUri.EndsWith('/') ? baseUrl.AbsoluteUri : baseUrl.AbsoluteUri + "/";
                client.BaseAddress = new Uri(absolute);
            }

            if (!string.IsNullOrWhiteSpace(research.DoclingApiKey))
            {
                client.DefaultRequestHeaders.Remove("X-Api-Key");
                client.DefaultRequestHeaders.TryAddWithoutValidation("X-Api-Key", research.DoclingApiKey);
            }
        });
        services.AddSingleton<IDocumentConverter>(provider => new DocumentConverter(
            provider.GetRequiredService<IHttpClientFactory>().CreateClient(DocumentConverter.HttpClientName),
            provider.GetRequiredService<IOptions<ResearchOptions>>()));
        services.AddAssistantTool<ReadSourceTool>();
        services.AddAssistantTool<ReadDocumentTool>();
        return services;
    }
}
