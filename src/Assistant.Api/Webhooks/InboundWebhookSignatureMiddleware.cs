using System.Buffers;
using Assistant.Abstractions.Webhooks;
using Microsoft.Extensions.Options;

namespace Assistant.Api.Webhooks;

/// <summary>
/// Verifies <c>POST /api/hooks/{source}</c> before endpoint routing.
/// Order: source, body size, headers, timestamp, signature, idempotency, then the handler.
/// </summary>
internal sealed class InboundWebhookSignatureMiddleware(
    StandardWebhooksVerifier standard,
    PebbleIndexSignatureVerifier pebble,
    IOptions<InboundWebhookOptions> options,
    ILogger<InboundWebhookSignatureMiddleware> logger,
    IInboundWebhookDeliveryStore store) : IMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        if (!HttpMethods.IsPost(context.Request.Method))
        {
            await next(context);
            return;
        }

        var sourceName = SourceFrom(context.Request.Path);
        if (sourceName is null || !TryGetSource(sourceName, out var source))
        {
            await Reject(context, StatusCodes.Status404NotFound, InboundWebhookCodes.UnknownSource, sourceName, null);
            return;
        }

        byte[] body;
        try
        {
            var read = await ReadBodyAsync(context, source.MaxBodyBytes);
            if (read is null)
            {
                await Reject(context, StatusCodes.Status413PayloadTooLarge, InboundWebhookCodes.BodyTooLarge, sourceName, null);
                return;
            }

            body = read;
        }
        catch (BadHttpRequestException)
        {
            await Reject(context, StatusCodes.Status413PayloadTooLarge, InboundWebhookCodes.BodyTooLarge, sourceName, null);
            return;
        }

        var headers = CopyHeaders(context.Request);
        var request = new InboundWebhookRequest(sourceName, headers, body);
        var secrets = DecodeSecrets(source.Secrets);
        IInboundSignatureVerifier verifier = string.Equals(source.Format, PebbleIndexSignatureVerifier.Format, StringComparison.Ordinal)
            ? pebble
            : standard;
        var result = verifier.Verify(request, secrets);
        if (result is not InboundSignatureResult.Valid valid)
        {
            var code = result is InboundSignatureResult.Invalid invalid
                ? invalid.Code
                : InboundWebhookCodes.SignatureInvalid;
            await Reject(context, StatusCodes.Status401Unauthorized, code, sourceName, DeliveryId(headers, source.Format));
            return;
        }

        var recorded = await store.TryRecordAsync(sourceName, valid.DeliveryId, valid.Timestamp, context.RequestAborted);
        if (!recorded)
        {
            await Reject(context, StatusCodes.Status409Conflict, InboundWebhookCodes.Replayed, sourceName, valid.DeliveryId);
            return;
        }

        context.Features.Set<IVerifiedInboundWebhookFeature>(
            new VerifiedInboundWebhookFeature(sourceName, valid.DeliveryId, valid.Timestamp));
        context.Request.Body.Position = 0;
        if (logger.IsEnabled(LogLevel.Information))
        {
            var loggedSource = LogSource(sourceName);
            var loggedId = LogId(valid.DeliveryId);
            InboundWebhookLog.Decided(logger, loggedSource, InboundWebhookCodes.Accepted, loggedId);
        }

        await next(context);
    }

    private bool TryGetSource(string sourceName, out InboundWebhookSourceOptions source)
    {
        foreach (var (name, candidate) in options.Value.Sources)
        {
            if (candidate.Enabled && string.Equals(name, sourceName, StringComparison.OrdinalIgnoreCase))
            {
                source = candidate;
                return true;
            }
        }

        source = null!;
        return false;
    }

    private static async Task<byte[]?> ReadBodyAsync(HttpContext context, int maxBodyBytes)
    {
        if (context.Request.ContentLength is long length && length > maxBodyBytes)
        {
            return null;
        }

        // One byte past the limit distinguishes "exactly MaxBodyBytes" from "too large"
        // without the buffering stream throwing on a legal body.
        var ceiling = maxBodyBytes == int.MaxValue ? maxBodyBytes : maxBodyBytes + 1;
        context.Request.EnableBuffering(
            bufferThreshold: Math.Min(maxBodyBytes, 64 * 1024),
            bufferLimit: ceiling);

        using var buffer = new MemoryStream();
        var rented = ArrayPool<byte>.Shared.Rent(16 * 1024);
        try
        {
            while (buffer.Length < ceiling)
            {
                var room = ceiling - (int)buffer.Length;
                var read = await context.Request.Body.ReadAsync(
                    rented.AsMemory(0, Math.Min(rented.Length, room)),
                    context.RequestAborted);
                if (read == 0)
                {
                    break;
                }

                buffer.Write(rented, 0, read);
            }

            if (buffer.Length > maxBodyBytes)
            {
                return null;
            }

            return buffer.ToArray();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented, clearArray: true);
        }
    }

    private async Task Reject(HttpContext context, int status, string code, string? source, string? deliveryId)
    {
        if (logger.IsEnabled(LogLevel.Information))
        {
            var loggedSource = LogSource(source);
            var loggedId = LogId(deliveryId);
            InboundWebhookLog.Decided(logger, loggedSource, code, loggedId);
        }
        context.Response.StatusCode = status;
        context.Response.ContentType = "text/plain";
        await context.Response.WriteAsync(code, context.RequestAborted);
    }

    private static List<byte[]> DecodeSecrets(IReadOnlyList<string>? secrets)
    {
        var keys = new List<byte[]>(capacity: 2);
        if (secrets is null)
        {
            return keys;
        }

        foreach (var secret in secrets)
        {
            if (WebhookSecrets.TryDecode(secret, out var key))
            {
                keys.Add(key);
            }
        }

        return keys;
    }

    private static Dictionary<string, string> CopyHeaders(HttpRequest request)
    {
        var headers = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var header in request.Headers)
        {
            headers[header.Key.ToLowerInvariant()] = header.Value.ToString();
        }

        return headers;
    }

    private static string? DeliveryId(Dictionary<string, string> headers, string format)
    {
        var name = string.Equals(format, PebbleIndexSignatureVerifier.Format, StringComparison.Ordinal)
            ? "x-index-delivery"
            : "webhook-id";
        return headers.TryGetValue(name, out var id) ? id : null;
    }

    internal static string? SourceFrom(PathString path)
    {
        if (!path.StartsWithSegments("/api/hooks", StringComparison.OrdinalIgnoreCase, out var rest))
        {
            return null;
        }

        var value = rest.Value;
        if (string.IsNullOrEmpty(value) || value == "/")
        {
            return null;
        }

        var trimmed = value.Trim('/');
        var slash = trimmed.IndexOf('/', StringComparison.Ordinal);
        if (slash >= 0)
        {
            trimmed = trimmed[..slash];
        }

        return trimmed.Length == 0 ? null : trimmed;
    }

    private static string LogSource(string? source)
    {
        if (string.IsNullOrEmpty(source))
        {
            return "-";
        }

        var trimmed = source.Length <= 64 ? source : source[..64];
        return trimmed
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", string.Empty, StringComparison.Ordinal);
    }

    private static string LogId(string? deliveryId)
    {
        if (string.IsNullOrEmpty(deliveryId))
        {
            return "-";
        }

        var trimmed = deliveryId.Length <= 8 ? deliveryId : deliveryId[..8];
        return trimmed
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", string.Empty, StringComparison.Ordinal);
    }
}
