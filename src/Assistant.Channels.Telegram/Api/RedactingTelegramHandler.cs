using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Assistant.Channels.Telegram.Api;

/// <summary>
/// Inserts the bot token into the request path and logs the call without it.
/// The client and the DTOs never see the token.
/// </summary>
public sealed class RedactingTelegramHandler(
    IOptionsMonitor<TelegramOptions> options,
    ILogger<RedactingTelegramHandler> logger) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var token = options.CurrentValue.BotToken;
        if (string.IsNullOrEmpty(token))
        {
            throw new TelegramApiException("Telegram is enabled but the bot token is empty.");
        }

        ApplyToken(request, token);
        var redactedPath = TelegramPathRedaction.Redact(request.RequestUri?.PathAndQuery ?? "");
        var apiMethod = ApiMethod(redactedPath);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var response = await base.SendAsync(request, cancellationToken);
            var (ok, errorCode) = await ReadOutcomeAsync(response, redactedPath, cancellationToken);
            var okText = FormatOk(ok);
            var errorText = errorCode?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "";
            TelegramLog.CallCompleted(
                logger,
                apiMethod,
                (int)response.StatusCode,
                stopwatch.ElapsedMilliseconds,
                okText,
                errorText,
                redactedPath);
            return response;
        }
        catch (Exception exception) when (exception is HttpRequestException or TimeoutException)
        {
            var exceptionType = exception.GetType().Name;
            TelegramLog.CallFailed(
                logger,
                apiMethod,
                stopwatch.ElapsedMilliseconds,
                exceptionType,
                redactedPath);
            throw new HttpRequestException("Telegram request failed before a response.");
        }
    }

    internal static void ApplyToken(HttpRequestMessage request, string token)
    {
        var uri = request.RequestUri ?? throw new InvalidOperationException("Telegram request URI is required.");
        if (!uri.IsAbsoluteUri)
        {
            uri = new Uri(new Uri("https://api.telegram.org/"), uri);
        }

        var path = uri.AbsolutePath;
        if (path.Contains("/bot", StringComparison.Ordinal))
        {
            return;
        }

        var rewritten = path.StartsWith("/file/", StringComparison.Ordinal)
            ? "/file/bot" + token + path["/file".Length..]
            : "/bot" + token + path;

        request.RequestUri = new Uri($"{uri.Scheme}://{uri.Authority}{rewritten}{uri.Query}");
    }

    private static string FormatOk(bool? ok) => ok switch
    {
        true => "true",
        false => "false",
        _ => "",
    };

    private static string ApiMethod(string redactedPath)
    {
        var path = redactedPath.Split('?', 2)[0];
        if (path.StartsWith("/file/", StringComparison.Ordinal))
        {
            return "download";
        }

        var slash = path.LastIndexOf('/');
        return slash >= 0 && slash < path.Length - 1 ? path[(slash + 1)..] : path;
    }

    private static async Task<(bool? Ok, int? ErrorCode)> ReadOutcomeAsync(
        HttpResponseMessage response, string redactedPath, CancellationToken cancellationToken)
    {
        if (redactedPath.StartsWith("/file/", StringComparison.Ordinal)
            || response.Content is null
            || response.Content.Headers.ContentLength == 0)
        {
            return (response.IsSuccessStatusCode, response.IsSuccessStatusCode ? null : (int)response.StatusCode);
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        var replacement = new ByteArrayContent(bytes);
        foreach (var header in response.Content.Headers)
        {
            replacement.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        response.Content.Dispose();
        response.Content = replacement;

        try
        {
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            bool? ok = root.TryGetProperty("ok", out var okProperty) && okProperty.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? okProperty.GetBoolean()
                : null;
            int? errorCode = root.TryGetProperty("error_code", out var codeProperty) && codeProperty.TryGetInt32(out var code)
                ? code
                : null;
            return (ok, errorCode);
        }
        catch (JsonException)
        {
            return (response.IsSuccessStatusCode, null);
        }
    }
}
