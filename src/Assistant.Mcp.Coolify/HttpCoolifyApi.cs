using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

namespace Assistant.Mcp.Coolify;

internal sealed class HttpCoolifyApi(
    HttpClient http,
    CoolifySettings settings,
    CoolifyAllowlist allowlist,
    ILogger<HttpCoolifyApi> logger) : ICoolifyApi
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public Task<CallToolResult> GetStatusAsync(string? app, CancellationToken cancellationToken)
        => SendAsync(
            app,
            uuid => $"/api/v1/applications/{Uri.EscapeDataString(uuid)}",
            HttpMethod.Get,
            ProjectStatus,
            cancellationToken);

    public Task<CallToolResult> GetLogsAsync(string? app, int? lines, CancellationToken cancellationToken)
    {
        if (lines is < 1)
        {
            return Task.FromResult(ToolResults.Fail("The lines argument must be at least 1."));
        }

        var count = Math.Min(lines ?? 100, 500);
        return SendAsync(
            app,
            uuid => $"/api/v1/applications/{Uri.EscapeDataString(uuid)}/logs?lines={count}",
            HttpMethod.Get,
            ProjectLogs,
            cancellationToken);
    }

    public Task<CallToolResult> ControlAsync(string? app, string? action, CancellationToken cancellationToken)
    {
        var normalized = action?.Trim().ToLowerInvariant();
        if (normalized is not ("start" or "stop" or "restart" or "deploy"))
        {
            return Task.FromResult(ToolResults.Fail("The action argument must be start, stop, restart, or deploy."));
        }

        return SendAsync(
            app,
            uuid => normalized == "deploy"
                ? $"/api/v1/deploy?uuid={Uri.EscapeDataString(uuid)}"
                : $"/api/v1/applications/{Uri.EscapeDataString(uuid)}/{normalized}",
            HttpMethod.Post,
            body => ProjectControl(app!.Trim(), normalized, body),
            cancellationToken);
    }

    private async Task<CallToolResult> SendAsync(
        string? app,
        Func<string, string> pathForUuid,
        HttpMethod method,
        Func<string, CallToolResult> project,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(app) || !allowlist.TryGetUuid(app.Trim(), out var uuid))
        {
            return ToolResults.Fail("The application is not in the Coolify allowlist.");
        }

        if (RejectConfiguration() is { } configurationError)
        {
            return configurationError;
        }

        try
        {
            using var request = new HttpRequestMessage(method, pathForUuid(uuid));
            if (method == HttpMethod.Post)
            {
                request.Content = new ByteArrayContent([]);
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            }

            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return ToolResults.Fail("Coolify rejected the token (401).");
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                CoolifyLog.HttpStatus(logger, (int)response.StatusCode);
                return ToolResults.Fail($"Coolify returned HTTP {(int)response.StatusCode}.");
            }

            return project(body);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ToolResults.Fail("The Coolify request timed out.");
        }
        catch (HttpRequestException ex)
        {
            CoolifyLog.RequestFailed(logger, ex);
            return ToolResults.Fail("The Coolify request failed.");
        }
        catch (JsonException ex)
        {
            CoolifyLog.InvalidPayload(logger, ex);
            return ToolResults.Fail("Coolify returned a payload that could not be read.");
        }
    }

    private CallToolResult? RejectConfiguration()
    {
        if (settings.Origin is null)
        {
            return ToolResults.Fail("COOLIFY_API_URL is not configured.");
        }

        if (settings.Token is null)
        {
            return ToolResults.Fail("COOLIFY_API_TOKEN is not configured.");
        }

        return null;
    }

    private static CallToolResult ProjectStatus(string body)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var view = new Dictionary<string, string?>
        {
            ["name"] = ReadString(root, "name"),
            ["status"] = ReadString(root, "status"),
            ["fqdn"] = ReadString(root, "fqdn"),
            ["updated"] = ReadString(root, "updated") ?? ReadString(root, "updated_at"),
        };

        return ToolResults.Ok(JsonSerializer.Serialize(view, Json));
    }

    private static CallToolResult ProjectLogs(string body)
    {
        var text = ExtractLogs(body);
        return ToolResults.Ok(LogRedactor.Filter(text));
    }

    private static CallToolResult ProjectControl(string app, string action, string body)
    {
        _ = body;
        return ToolResults.Ok(JsonSerializer.Serialize(new { app, action, accepted = true }, Json));
    }

    private static string ExtractLogs(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.String)
            {
                return root.GetString() ?? string.Empty;
            }

            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("logs", out var logs))
            {
                if (logs.ValueKind == JsonValueKind.String)
                {
                    return logs.GetString() ?? string.Empty;
                }

                if (logs.ValueKind == JsonValueKind.Array)
                {
                    var lines = new List<string>();
                    foreach (var item in logs.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String)
                        {
                            lines.Add(item.GetString() ?? string.Empty);
                        }
                    }

                    return string.Join('\n', lines);
                }
            }
        }
        catch (JsonException)
        {
            return body;
        }

        return body;
    }

    private static string? ReadString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return value.GetString();
    }
}
