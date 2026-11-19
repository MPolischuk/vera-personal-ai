namespace Assistant.Mcp.Coolify;

internal sealed class CoolifySettings
{
    public const int HttpTimeoutSeconds = 30;

    public Uri? Origin { get; init; }

    public string? Token { get; init; }

    public static CoolifySettings From(IConfiguration configuration)
    {
        var rawUrl = configuration["COOLIFY_API_URL"];
        Uri? origin = null;
        if (!string.IsNullOrWhiteSpace(rawUrl)
            && Uri.TryCreate(rawUrl.Trim().TrimEnd('/'), UriKind.Absolute, out var parsed)
            && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps))
        {
            origin = parsed;
        }

        var token = configuration["COOLIFY_API_TOKEN"];
        return new CoolifySettings
        {
            Origin = origin,
            Token = string.IsNullOrWhiteSpace(token) ? null : token.Trim(),
        };
    }
}
