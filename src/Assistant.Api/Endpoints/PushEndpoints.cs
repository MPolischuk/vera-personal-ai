using System.Buffers.Text;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Assistant.Abstractions.Channels;
using Assistant.Abstractions.Push;
using Assistant.Api.Authentication;
using Assistant.Channels.WebPush;
using Microsoft.Extensions.Options;

namespace Assistant.Api.Endpoints;

internal static class PushEndpoints
{
    public static IEndpointRouteBuilder MapPushEndpoints(this IEndpointRouteBuilder group)
    {
        group.MapGet("/push/vapid-public-key", PublicKeyAsync);
        group.MapPut("/push/subscriptions", UpsertAsync);
        group.MapGet("/push/subscriptions", ListAsync);
        group.MapDelete("/push/subscriptions/current", DeleteCurrentAsync);
        group.MapPost("/push/test", TestAsync);
        return group;
    }

    private static IResult PublicKeyAsync(IOptions<WebPushOptions> options)
    {
        if (!options.Value.Enabled)
        {
            return Results.NotFound();
        }

        return Results.Ok(new VapidPublicKeyResponse(options.Value.VapidPublicKey));
    }

    private static async Task<IResult> UpsertAsync(
        PushSubscriptionBody? body,
        ClaimsPrincipal user,
        HttpContext http,
        IOptions<WebPushOptions> options,
        CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            return Results.NotFound();
        }

        if (!TryDeviceId(user, out var deviceId))
        {
            return Problem(StatusCodes.Status409Conflict, "A device is required.", "device_required");
        }

        if (!TryReadSubscription(body, out var endpoint, out var p256dh, out var auth))
        {
            return Problem(StatusCodes.Status400BadRequest, "The push subscription is invalid.", "subscription_invalid");
        }

        if (!PushEndpointGuard.IsAllowed(endpoint, options.Value.AllowedEndpointHosts))
        {
            return Problem(
                StatusCodes.Status422UnprocessableEntity,
                "The push service is not supported.",
                "push_service_unsupported");
        }

        var store = http.RequestServices.GetRequiredService<IPushSubscriptionReader>();
        await store.UpsertAsync(
            user.UserId(),
            deviceId,
            endpoint,
            p256dh,
            auth,
            HashKey(options.Value.VapidPublicKey),
            UserAgentFamily(http.Request.Headers.TryGetValue("User-Agent", out var userAgent) ? userAgent.ToString() : null),
            ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ListAsync(
        ClaimsPrincipal user,
        HttpContext http,
        IOptions<WebPushOptions> options,
        CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            return Results.NotFound();
        }

        var store = http.RequestServices.GetRequiredService<IPushSubscriptionReader>();
        var rows = await store.ListSummariesAsync(user.UserId(), ct);
        return Results.Ok(new PushSubscriptionListResponse(rows.Select(ToResponse).ToArray()));
    }

    private static async Task<IResult> DeleteCurrentAsync(
        ClaimsPrincipal user,
        HttpContext http,
        IOptions<WebPushOptions> options,
        CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            return Results.NotFound();
        }

        if (!TryDeviceId(user, out var deviceId))
        {
            return Problem(StatusCodes.Status409Conflict, "A device is required.", "device_required");
        }

        var store = http.RequestServices.GetRequiredService<IPushSubscriptionReader>();
        await store.RevokeCurrentAsync(user.UserId(), deviceId, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> TestAsync(
        ClaimsPrincipal user,
        HttpContext http,
        IOptions<WebPushOptions> options,
        IOptions<DiagnosticsOptions> diagnostics,
        CancellationToken ct)
    {
        if (!diagnostics.Value.EnableTestEndpoints || !options.Value.Enabled)
        {
            return Results.NotFound();
        }

        if (!TryDeviceId(user, out var deviceId))
        {
            return Problem(StatusCodes.Status409Conflict, "A device is required.", "device_required");
        }

        var channel = http.RequestServices
            .GetServices<IOutboundChannel>()
            .FirstOrDefault(item => item.Id == ChannelId.WebPush);
        if (channel is null)
        {
            return Results.NotFound();
        }

        var result = await channel.SendAsync(
            new OutboundMessage(
                MessageId: Guid.CreateVersion7(),
                Channel: ChannelId.WebPush,
                UserId: user.UserId(),
                TargetRef: deviceId.ToString("D"),
                Kind: OutboundKind.Notification,
                Text: "Si ves esto, las notificaciones de este dispositivo funcionan.",
                Title: "Prueba"),
            ct);

        return result switch
        {
            OutboundResult.Delivered => Results.NoContent(),
            OutboundResult.NotApplicable => Results.NotFound(),
            _ => Problem(StatusCodes.Status502BadGateway, "The push service rejected the test.", "push_failed"),
        };
    }

    private static bool TryReadSubscription(
        PushSubscriptionBody? body,
        out string endpoint,
        out string p256dh,
        out string auth)
    {
        endpoint = "";
        p256dh = "";
        auth = "";
        if (!IsHttpsEndpoint(body?.Endpoint) || body!.Keys is null)
        {
            return false;
        }

        if (!IsKey(body.Keys.P256dh, 65, uncompressed: true) || !IsKey(body.Keys.Auth, 16, uncompressed: false))
        {
            return false;
        }

        endpoint = body.Endpoint!;
        p256dh = body.Keys.P256dh!;
        auth = body.Keys.Auth!;
        return true;
    }

    private static bool IsHttpsEndpoint(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 1000)
        {
            return false;
        }

        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && !string.IsNullOrEmpty(uri.Host);
    }

    private static bool IsKey(string? value, int expectedLength, bool uncompressed)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 200)
        {
            return false;
        }

        try
        {
            var bytes = Base64Url.DecodeFromChars(value);
            return bytes.Length == expectedLength && (!uncompressed || bytes[0] == 0x04);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string HashKey(string publicKey)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(publicKey));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    internal static string? UserAgentFamily(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return null;
        }

        var agent = userAgent;
        if (Contains(agent, "iPhone") || Contains(agent, "iPad") || Contains(agent, "iPod"))
        {
            return "ios-safari";
        }

        if (Contains(agent, "Android"))
        {
            if (Contains(agent, "Firefox"))
            {
                return "firefox-android";
            }

            if (Contains(agent, "Edg"))
            {
                return "edge-android";
            }

            if (Contains(agent, "Chrome"))
            {
                return "chrome-android";
            }

            return "other";
        }

        if (Contains(agent, "Firefox"))
        {
            return "firefox-desktop";
        }

        if (Contains(agent, "Edg"))
        {
            return "edge-desktop";
        }

        if (Contains(agent, "Chrome"))
        {
            return "chrome-desktop";
        }

        if (Contains(agent, "Safari") && Contains(agent, "Macintosh"))
        {
            return "safari-desktop";
        }

        return "other";
    }

    private static bool Contains(string value, string fragment)
        => value.Contains(fragment, StringComparison.OrdinalIgnoreCase);

    private static bool TryDeviceId(ClaimsPrincipal user, out Guid deviceId)
    {
        var raw = user.FindFirstValue("device_id");
        return Guid.TryParse(raw, out deviceId) && deviceId != Guid.Empty;
    }

    private static PushSubscriptionResponse ToResponse(PushSubscriptionSummary row)
        => new(row.Id, row.UserAgentFamily, row.Status, row.CreatedAt, row.LastSuccessAt, row.LastFailureAt);

    private static IResult Problem(int status, string title, string code)
        => Results.Problem(
            title: title,
            statusCode: status,
            extensions: new Dictionary<string, object?> { ["code"] = code });

    private sealed record VapidPublicKeyResponse(string PublicKey);

    private sealed record PushSubscriptionBody(
        string? Endpoint,
        PushSubscriptionKeysBody? Keys);

    private sealed record PushSubscriptionKeysBody(
        [property: JsonPropertyName("p256dh")] string? P256dh,
        string? Auth);

    private sealed record PushSubscriptionListResponse(IReadOnlyList<PushSubscriptionResponse> Subscriptions);

    private sealed record PushSubscriptionResponse(
        Guid Id,
        string? UserAgentFamily,
        string Status,
        DateTimeOffset CreatedAt,
        DateTimeOffset? LastSuccessAt,
        DateTimeOffset? LastFailureAt);
}
