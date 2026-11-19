using System.Net.Http.Headers;
using System.Text.Json;
using Assistant.Abstractions.Proactive;
using Assistant.Abstractions.Security;
using Microsoft.Extensions.DependencyInjection;

namespace Assistant.Extensions.Mcp;

/// <summary>
/// Exchanges the stored calendar refresh token for an access token.
/// The access token stays in memory. A failure returns null and the server stays disconnected.
/// </summary>
internal sealed class McpAccessTokenRefresher(IServiceScopeFactory scopes, TimeProvider clock) : IMcpAccessTokenRefresher
{
    public const string TokenEndpoint = "https://oauth2.googleapis.com/token";

    public const string ClientIdVariable = "CALENDAR_OAUTH_CLIENT_ID";

    public const string ClientSecretVariable = "CALENDAR_OAUTH_CLIENT_SECRET";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    public async ValueTask<string?> GetAccessTokenAsync(string serverId, CancellationToken ct)
    {
        if (!string.Equals(serverId, CalendarWriteTools.ServerId, StringComparison.Ordinal))
        {
            return null;
        }

        var clientId = Environment.GetEnvironmentVariable(ClientIdVariable);
        var clientSecret = Environment.GetEnvironmentVariable(ClientSecretVariable);
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            return null;
        }

        await using var scope = scopes.CreateAsyncScope();
        var store = scope.ServiceProvider.GetService<IMcpOAuthTokenStore>();
        var box = scope.ServiceProvider.GetService<ISecretBox>();
        if (store is null || box is null)
        {
            return null;
        }

        var credential = await store.FindSingleAsync(serverId, ct);
        if (credential is null)
        {
            return null;
        }

        string refresh;
        try
        {
            refresh = box.Unprotect(credential.RefreshProtected);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(refresh))
        {
            return null;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refresh,
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
            }),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        HttpResponseMessage response;
        try
        {
            response = await Http.SendAsync(request, ct);
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null;
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (!document.RootElement.TryGetProperty("access_token", out var accessProperty)
                || accessProperty.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var access = accessProperty.GetString();
            if (string.IsNullOrWhiteSpace(access))
            {
                return null;
            }

            if (document.RootElement.TryGetProperty("refresh_token", out var rotated)
                && rotated.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(rotated.GetString()))
            {
                await store.SaveRefreshAsync(
                    credential.UserId,
                    credential.ServerId,
                    box.Protect(rotated.GetString()!),
                    clock.GetUtcNow(),
                    ct);
            }

            return access;
        }
    }
}
