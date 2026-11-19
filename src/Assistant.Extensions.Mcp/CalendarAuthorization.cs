using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Authentication;

namespace Assistant.Extensions.Mcp;

/// <summary>
/// Authorization URL for the calendar MCP. <see cref="ClientOAuthProvider"/> is internal in
/// the 2.3.1 package; these are the same query keys it applies, including
/// <c>access_type=offline</c> via <see cref="ClientOAuthOptions.AdditionalAuthorizationParameters"/>
/// and PKCE <c>S256</c>.
/// </summary>
public static class CalendarAuthorization
{
    public const string AuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";

    public const string TokenEndpoint = "https://oauth2.googleapis.com/token";

    public const string ClientIdVariable = "CALENDAR_OAUTH_CLIENT_ID";

    public const string ClientSecretVariable = "CALENDAR_OAUTH_CLIENT_SECRET";

    public const string RedirectUriVariable = "CALENDAR_OAUTH_REDIRECT_URI";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    public static readonly string[] Scopes =
    [
        "https://www.googleapis.com/auth/calendar.calendarlist.readonly",
        "https://www.googleapis.com/auth/calendar.events.readonly",
        "https://www.googleapis.com/auth/calendar.events.freebusy",
    ];

    public static ClientOAuthOptions CreateOptions(Uri redirectUri, string clientId, string? clientSecret)
        => new()
        {
            RedirectUri = redirectUri,
            ClientId = clientId,
            ClientSecret = clientSecret,
            Scopes = Scopes,
            AdditionalAuthorizationParameters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["access_type"] = "offline",
                ["prompt"] = "consent",
            },
        };

    /// <summary>
    /// Exchanges an authorization code. Returns the refresh token only.
    /// The access token is discarded and nothing is logged.
    /// </summary>
    public static async ValueTask<string?> ExchangeCodeAsync(
        string code,
        string codeVerifier,
        string redirectUri,
        string clientId,
        string clientSecret,
        CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["code_verifier"] = codeVerifier,
                ["redirect_uri"] = redirectUri,
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
            if (!document.RootElement.TryGetProperty("refresh_token", out var refresh)
                || refresh.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var value = refresh.GetString();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
    }

    public static Uri BuildAuthorizationUri(ClientOAuthOptions options, string state, string codeChallenge)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ClientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(codeChallenge);

        var query = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["client_id"] = options.ClientId,
            ["redirect_uri"] = options.RedirectUri.ToString(),
            ["response_type"] = "code",
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256",
            ["state"] = state,
            ["scope"] = string.Join(' ', options.Scopes ?? []),
        };

        foreach (var pair in options.AdditionalAuthorizationParameters)
        {
            if (query.ContainsKey(pair.Key))
            {
                throw new ArgumentException(
                    "An additional authorization parameter cannot replace a PKCE or redirect field.",
                    nameof(options));
            }

            query.Add(pair.Key, pair.Value);
        }

        var builder = new StringBuilder(AuthorizationEndpoint);
        builder.Append('?');
        var first = true;
        foreach (var pair in query)
        {
            if (!first)
            {
                builder.Append('&');
            }

            first = false;
            builder.Append(Uri.EscapeDataString(pair.Key));
            builder.Append('=');
            builder.Append(Uri.EscapeDataString(pair.Value));
        }

        return new Uri(builder.ToString());
    }

    public static (string Verifier, string Challenge) CreatePkce()
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        return (verifier, challenge);
    }

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
