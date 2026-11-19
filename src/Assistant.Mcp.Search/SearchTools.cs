using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Assistant.Mcp.Search;

[McpServerToolType]
internal sealed class SearchTools(IHttpClientFactory httpClientFactory, ILogger<SearchTools>? logger = null)
{
    public const string HttpClientName = "searxng";

    public const int MaxResults = 5;

    public const string UnavailableMessage = "la búsqueda no está disponible";

    public const string EmptyMessage = "sin resultados";

    [McpServerTool(Name = "query", ReadOnly = true, OpenWorld = true)]
    [Description("Busca en la web y devuelve título, URL y snippet. No abre las páginas.")]
    public async Task<CallToolResult> Query(
        [Description("Texto a buscar.")] string q,
        CancellationToken cancellationToken,
        [Description("Código de idioma. Por defecto es.")] string? lang = null,
        [Description("Rango de tiempo: day, week, month o year.")] string? time_range = null)
    {
        var http = httpClientFactory.CreateClient(HttpClientName);
        if (string.IsNullOrWhiteSpace(q) || http.BaseAddress is null)
        {
            if (logger is not null)
            {
                SearchLog.UnavailableBeforeRequest(
                    logger,
                    string.IsNullOrWhiteSpace(q),
                    http.BaseAddress is null);
            }

            return Fail();
        }

        var path = BuildPath(q, lang, time_range);
        try
        {
            using var response = await http.GetAsync(path, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                if (logger is not null)
                {
                    SearchLog.UnavailableStatus(logger, (int)response.StatusCode);
                }

                return Fail();
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var text = Format(json);
            if (text is null)
            {
                if (logger is not null)
                {
                    SearchLog.UnavailableBody(logger);
                }

                return Fail();
            }

            return Ok(text);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            if (logger is not null)
            {
                SearchLog.UnavailableTimeout(logger);
            }

            return Fail();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            if (logger is not null)
            {
                SearchLog.UnavailableException(logger, exception.GetType().Name);
            }

            return Fail();
        }
    }

    internal static string BuildPath(string query, string? lang, string? timeRange)
    {
        var language = NormalizeLanguage(lang);
        var path = "/search?q=" + Uri.EscapeDataString(query.Trim())
            + "&language=" + Uri.EscapeDataString(language)
            + "&format=json";
        if (NormalizeTimeRange(timeRange) is { } range)
        {
            path += "&time_range=" + range;
        }

        return path;
    }

    private static string? Format(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("results", out var results)
                || results.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var builder = new StringBuilder();
            var count = 0;
            foreach (var item in results.EnumerateArray())
            {
                if (count == MaxResults)
                {
                    break;
                }

                var title = Read(item, "title");
                var url = Read(item, "url");
                var snippet = Read(item, "content");
                if (title is null && url is null && snippet is null)
                {
                    continue;
                }

                count++;
                if (builder.Length > 0)
                {
                    builder.Append("\n\n");
                }

                builder.Append(count.ToString(CultureInfo.InvariantCulture));
                builder.Append(". ");
                builder.Append(title ?? "");
                builder.Append('\n');
                builder.Append(url ?? "");
                builder.Append('\n');
                builder.Append(snippet ?? "");
            }

            return count == 0 ? EmptyMessage : builder.ToString();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Read(JsonElement item, string name)
    {
        if (item.ValueKind != JsonValueKind.Object
            || !item.TryGetProperty(name, out var value)
            || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = value.GetString();
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static string NormalizeLanguage(string? lang)
    {
        if (string.IsNullOrWhiteSpace(lang))
        {
            return "es";
        }

        var text = lang.Trim();
        if (text.Length is < 2 or > 16)
        {
            return "es";
        }

        foreach (var character in text)
        {
            if (character is not ((>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or '-'))
            {
                return "es";
            }
        }

        return text;
    }

    private static string? NormalizeTimeRange(string? timeRange)
    {
        if (string.IsNullOrWhiteSpace(timeRange))
        {
            return null;
        }

        return timeRange.Trim().ToLowerInvariant() switch
        {
            "day" or "week" or "month" or "year" => timeRange.Trim().ToLowerInvariant(),
            _ => null,
        };
    }

    private static CallToolResult Ok(string text) => new()
    {
        IsError = false,
        Content = [new TextContentBlock { Text = text }],
    };

    private static CallToolResult Fail() => new()
    {
        IsError = true,
        Content = [new TextContentBlock { Text = UnavailableMessage }],
    };
}
