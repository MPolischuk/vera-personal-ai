namespace Assistant.Mcp.Search;

internal static class SearchSettings
{
    public const int TimeoutSeconds = 20;

    public static Uri? Origin(IConfiguration configuration)
    {
        var raw = configuration["SEARXNG_URL"];
        if (string.IsNullOrWhiteSpace(raw)
            || !Uri.TryCreate(raw.Trim().TrimEnd('/') + "/", UriKind.Absolute, out var parsed)
            || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
        {
            return null;
        }

        return parsed;
    }
}
