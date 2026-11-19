using System.Buffers;
using System.Text;
using System.Threading.RateLimiting;
using Assistant.Abstractions.Channels;
using Assistant.Channels.Telegram.Inbound;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Assistant.Channels.Telegram.Webhook;

/// <summary>Anonymous <c>POST /webhooks/telegram</c>. Returns before the inbox handler runs.</summary>
public static class TelegramWebhookEndpoint
{
    public const string RateLimitPolicy = "telegram-webhook";

    public const string SecretHeaderName = "X-Telegram-Bot-Api-Secret-Token";

    public const int MaxBodyBytes = 1_048_576;

    public static void AddRateLimiter(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = (context, _) =>
            {
                var telemetry = context.HttpContext.RequestServices.GetService<IChannelIngressTelemetry>();
                telemetry?.WebhookRejected(ChannelId.Telegram, "rate");
                return ValueTask.CompletedTask;
            };
            options.AddPolicy(RateLimitPolicy, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 60,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    }));
        });
    }

    public static IEndpointRouteBuilder MapTelegramWebhook(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapPost(TelegramWebhookAddress.Path, ReceiveAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicy);
        return endpoints;
    }

    private static async Task<IResult> ReceiveAsync(
        HttpContext httpContext,
        IOptions<TelegramOptions> options,
        TelegramChannelGate gate,
        IInboxAcceptor acceptor,
        IChannelInboxStore inbox,
        TelegramStopHandler stops,
        IChannelIngressTelemetry telemetry,
        TimeProvider clock,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger(typeof(TelegramWebhookEndpoint).FullName ?? nameof(TelegramWebhookEndpoint));
        if (gate.IsDisabled)
        {
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        var provided = httpContext.Request.Headers[SecretHeaderName].ToString();
        if (!TelegramWebhookSecret.Matches(options.Value.WebhookSecret, provided))
        {
            telemetry.WebhookRejected(ChannelId.Telegram, "secret");
            return Results.Unauthorized();
        }

        if (httpContext.Request.ContentLength is > MaxBodyBytes)
        {
            telemetry.WebhookRejected(ChannelId.Telegram, "size");
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        var chunk = ArrayPool<byte>.Shared.Rent(16 * 1024);
        try
        {
            await using var body = new MemoryStream();
            while (true)
            {
                var count = await httpContext.Request.Body.ReadAsync(chunk.AsMemory(0, chunk.Length), cancellationToken);
                if (count == 0)
                {
                    break;
                }

                if (body.Length + count > MaxBodyBytes)
                {
                    telemetry.WebhookRejected(ChannelId.Telegram, "size");
                    return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
                }

                body.Write(chunk, 0, count);
            }

            var json = Encoding.UTF8.GetString(body.GetBuffer().AsSpan(0, (int)body.Length));
            if (!TelegramUpdateEnvelopeFactory.TryCreate(json, clock.GetUtcNow(), out var parsed))
            {
                TelegramWebhookLog.Unparseable(logger);
                telemetry.Inbound(ChannelId.Telegram, "ignored");
                return Results.Ok();
            }

            var acceptance = await acceptor.AcceptAsync(parsed.Envelope, parsed.Kind, cancellationToken);
            await stops.AfterAcceptAsync(acceptance, parsed.Kind, json, inbox, cancellationToken);
            if (acceptance.Inserted && acceptance.InboxId is long inboxId && logger.IsEnabled(LogLevel.Information))
            {
                var peerRef = ChannelPeerLogRef.Format(parsed.Envelope.Channel, parsed.Envelope.ExternalPeerId);
                TelegramWebhookLog.Queued(logger, inboxId, peerRef);
            }

            return Results.Ok();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(chunk, clearArray: true);
        }
    }
}
