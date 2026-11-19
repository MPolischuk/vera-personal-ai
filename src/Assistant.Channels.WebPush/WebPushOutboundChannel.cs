using System.Net;
using System.Security.Cryptography;
using System.Text;
using Assistant.Abstractions.Channels;
using Assistant.Abstractions.Notifications;
using Assistant.Abstractions.Push;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Assistant.Channels.WebPush;

/// <summary>
/// Delivers a declarative Web Push to every active subscription of the user.
/// <see cref="OutboundResult.Delivered"/> when at least one push service responds 201.
/// Provider failures become results; this method does not throw for them.
/// </summary>
internal sealed class WebPushOutboundChannel : IOutboundChannel, IDisposable
{
    public const string HttpClientName = "webpush";

    private readonly HttpClient _httpClient;
    private readonly IPushSubscriptionReader _subscriptions;
    private readonly INotificationStore _notifications;
    private readonly ILogger<WebPushOutboundChannel> _logger;
    private readonly string? _publicBaseUrl;
    private readonly IReadOnlyList<string> _allowedHosts;
    private readonly VapidAuthentication _authentication;
    private readonly PushServiceClient _client;

    public WebPushOutboundChannel(
        HttpClient httpClient,
        IPushSubscriptionReader subscriptions,
        INotificationStore notifications,
        IOptions<WebPushOptions> options,
        string? publicBaseUrl,
        ILogger<WebPushOutboundChannel> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(subscriptions);
        ArgumentNullException.ThrowIfNull(notifications);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClient = httpClient;
        _subscriptions = subscriptions;
        _notifications = notifications;
        _logger = logger;
        _publicBaseUrl = string.IsNullOrWhiteSpace(publicBaseUrl) ? null : publicBaseUrl.Trim().TrimEnd('/');

        var current = options.Value;
        _allowedHosts = current.AllowedEndpointHosts ?? WebPushOptions.DefaultAllowedEndpointHosts;
        _authentication = new VapidAuthentication(current.VapidPublicKey, current.VapidPrivateKey)
        {
            Subject = current.VapidSubject,
        };
        _client = new PushServiceClient(_httpClient)
        {
            AutoRetryAfter = false,
            DefaultAuthentication = _authentication,
        };
    }

    public ChannelId Id => ChannelId.WebPush;

    public ChannelCapabilities Capabilities { get; } = new()
    {
        SupportsInbound = false,
        SupportsNotificationActions = true,
        SupportsSilentDelivery = false,
        MaxTextLength = WebPushPayloadBuilder.MaxBodyCharacters,
        MaxButtons = 2,
        MaxButtonLabelLength = 40,
        MaxActionPayloadBytes = 64,
        TextFormat = ChannelTextFormat.Plain,
    };

    public async ValueTask<ChannelReachability> GetReachabilityAsync(Guid userId, CancellationToken ct)
    {
        if (!await _subscriptions.HasActiveAsync(userId, ct).ConfigureAwait(false))
        {
            return new ChannelReachability(false, null, true, "no_subscription");
        }

        return new ChannelReachability(true, $"webpush:{userId:D}", true, null);
    }

    public async ValueTask<OutboundResult> SendAsync(OutboundMessage message, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (InvalidActions(message) is { } invalid)
        {
            WebPushLog.ActionsInvalid(_logger);
            return invalid;
        }

        var targets = await ResolveTargetsAsync(message, ct).ConfigureAwait(false);
        if (targets.Count == 0)
        {
            return new OutboundResult.NotApplicable("no_subscription");
        }

        var kind = NotificationKind.System;
        Guid? conversationId = null;
        if (message.NotificationId is Guid notificationId)
        {
            var stored = await _notifications.FindAsync(notificationId, ct).ConfigureAwait(false);
            if (stored is not null)
            {
                kind = stored.Kind;
                conversationId = stored.ConversationId;
            }
        }

        var headers = WebPushDispatchHeaders.For(kind, WebPushPayloadBuilder.TopicSource(message));
        var payload = WebPushPayloadBuilder.Build(
            message,
            kind,
            conversationId,
            _publicBaseUrl,
            WebPushPayloadBuilder.MaxBytes);
        var retryPayload = WebPushPayloadBuilder.Build(
            message,
            kind,
            conversationId,
            _publicBaseUrl,
            WebPushPayloadBuilder.RetryBytes);

        var delivered = false;
        var vapidRejected = 0;
        var attempted = 0;
        OutboundResult.TransientFailure? transient = null;
        OutboundResult.PermanentFailure? permanent = null;

        foreach (var target in targets)
        {
            ct.ThrowIfCancellationRequested();
            attempted++;
            var attempt = await SendOneAsync(target, payload, retryPayload, headers, shrunk: false, ct)
                .ConfigureAwait(false);
            switch (attempt.Result)
            {
                case OutboundResult.Delivered:
                    delivered = true;
                    break;
                case OutboundResult.TransientFailure failure:
                    if (transient is null || (transient.RetryAfter is null && failure.RetryAfter is not null))
                    {
                        transient = failure;
                    }

                    break;
                case OutboundResult.PermanentFailure failure:
                    permanent ??= failure;
                    if (attempt.VapidRejected)
                    {
                        vapidRejected++;
                    }

                    break;
            }
        }

        if (delivered)
        {
            return new OutboundResult.Delivered(ExternalId: null);
        }

        if (attempted > 0 && vapidRejected == attempted)
        {
            WebPushLog.VapidRejectedForAll(_logger);
        }

        if (transient is not null)
        {
            return transient;
        }

        return permanent ?? new OutboundResult.PermanentFailure("push_failed", DeactivateTarget: false);
    }

    public void Dispose() => _authentication.Dispose();

    private async ValueTask<IReadOnlyList<PushSubscriptionTarget>> ResolveTargetsAsync(
        OutboundMessage message,
        CancellationToken ct)
    {
        if (Guid.TryParse(message.TargetRef, out var deviceId) && deviceId != Guid.Empty)
        {
            return await _subscriptions.ListActiveTargetsByDeviceAsync(message.UserId, deviceId, ct)
                .ConfigureAwait(false);
        }

        return await _subscriptions.ListActiveTargetsAsync(message.UserId, ct).ConfigureAwait(false);
    }

    private async ValueTask<Attempt> SendOneAsync(
        PushSubscriptionTarget target,
        string payload,
        string retryPayload,
        WebPushDispatchHeaders headers,
        bool shrunk,
        CancellationToken ct)
    {
        var hash = ShortHash(target.Endpoint);
        if (!PushEndpointGuard.IsAllowed(target.Endpoint, _allowedHosts))
        {
            WebPushLog.Failed(_logger, "push_service_unsupported", hash);
            await _subscriptions.RecordDeliveryAsync(target.Id, PushDeliveryEffect.Disabled, ct).ConfigureAwait(false);
            return new Attempt(new OutboundResult.PermanentFailure("push_service_unsupported", DeactivateTarget: true), false);
        }

        try
        {
            var subscription = new PushSubscription { Endpoint = target.Endpoint };
            subscription.SetKey(PushEncryptionKeyName.P256DH, target.P256dh);
            subscription.SetKey(PushEncryptionKeyName.Auth, target.Auth);

            var push = new PushMessage(payload)
            {
                TimeToLive = headers.TtlSeconds,
                Urgency = MapUrgency(headers.Urgency),
            };
            if (!string.IsNullOrEmpty(headers.Topic))
            {
                push.Topic = headers.Topic;
            }

            await _client.RequestPushMessageDeliveryAsync(subscription, push, ct).ConfigureAwait(false);
            await _subscriptions.RecordDeliveryAsync(target.Id, PushDeliveryEffect.Succeeded, ct).ConfigureAwait(false);
            WebPushLog.Accepted(_logger, hash);
            return new Attempt(new OutboundResult.Delivered(ExternalId: null), VapidRejected: false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            WebPushLog.Failed(_logger, "timeout", hash);
            await _subscriptions.RecordDeliveryAsync(target.Id, PushDeliveryEffect.Failed, ct).ConfigureAwait(false);
            return new Attempt(new OutboundResult.TransientFailure("timeout", RetryAfter: null), false);
        }
        catch (PushServiceClientException exception)
        {
            return await MapStatusAsync(target, retryPayload, headers, shrunk, exception, hash, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            WebPushLog.Failed(_logger, "network", hash);
            await _subscriptions.RecordDeliveryAsync(target.Id, PushDeliveryEffect.Failed, ct).ConfigureAwait(false);
            return new Attempt(new OutboundResult.TransientFailure("network", RetryAfter: null), false);
        }
        catch (Exception exception) when (exception is InvalidOperationException or CryptographicException or ArgumentException)
        {
            WebPushLog.Failed(_logger, "subscription_invalid", hash);
            await _subscriptions.RecordDeliveryAsync(target.Id, PushDeliveryEffect.Failed, ct).ConfigureAwait(false);
            return new Attempt(new OutboundResult.PermanentFailure("subscription_invalid", DeactivateTarget: false), false);
        }
    }

    private async ValueTask<Attempt> MapStatusAsync(
        PushSubscriptionTarget target,
        string retryPayload,
        WebPushDispatchHeaders headers,
        bool shrunk,
        PushServiceClientException exception,
        string hash,
        CancellationToken ct)
    {
        var status = (int)exception.StatusCode;
        WebPushLog.Rejected(_logger, status, hash);
        if (status == (int)HttpStatusCode.RequestEntityTooLarge && !shrunk)
        {
            return await SendOneAsync(target, retryPayload, retryPayload, headers, shrunk: true, ct)
                .ConfigureAwait(false);
        }

        switch (status)
        {
            case 401:
            case 403:
                await _subscriptions.RecordDeliveryAsync(target.Id, PushDeliveryEffect.Disabled, ct).ConfigureAwait(false);
                return new Attempt(new OutboundResult.PermanentFailure(status.ToString(System.Globalization.CultureInfo.InvariantCulture), true), true);
            case 404:
            case 410:
                await _subscriptions.RecordDeliveryAsync(target.Id, PushDeliveryEffect.Revoked, ct).ConfigureAwait(false);
                return new Attempt(new OutboundResult.PermanentFailure(status.ToString(System.Globalization.CultureInfo.InvariantCulture), true), false);
            case 429:
                await _subscriptions.RecordDeliveryAsync(target.Id, PushDeliveryEffect.Failed, ct).ConfigureAwait(false);
                return new Attempt(new OutboundResult.TransientFailure("429", WebPushRetryAfterHint.Take(target.Endpoint)), false);
            case >= 500:
                await _subscriptions.RecordDeliveryAsync(target.Id, PushDeliveryEffect.Failed, ct).ConfigureAwait(false);
                return new Attempt(new OutboundResult.TransientFailure(status.ToString(System.Globalization.CultureInfo.InvariantCulture), null), false);
            default:
                await _subscriptions.RecordDeliveryAsync(target.Id, PushDeliveryEffect.Failed, ct).ConfigureAwait(false);
                return new Attempt(
                    new OutboundResult.PermanentFailure(status.ToString(System.Globalization.CultureInfo.InvariantCulture), false),
                    false);
        }
    }

    private OutboundResult.PermanentFailure? InvalidActions(OutboundMessage message)
    {
        var actions = message.Actions;
        if (actions is null || actions.Count == 0)
        {
            return null;
        }

        if (actions.Count > Capabilities.MaxButtons)
        {
            return new OutboundResult.PermanentFailure("actions_invalid", DeactivateTarget: false);
        }

        foreach (var action in actions)
        {
            if (string.IsNullOrEmpty(action.ActionId)
                || Encoding.UTF8.GetByteCount(action.ActionId) > Capabilities.MaxActionPayloadBytes)
            {
                return new OutboundResult.PermanentFailure("actions_invalid", DeactivateTarget: false);
            }
        }

        return null;
    }

    private static PushMessageUrgency MapUrgency(string urgency)
        => urgency switch
        {
            "high" => PushMessageUrgency.High,
            "low" => PushMessageUrgency.Low,
            _ => PushMessageUrgency.Normal,
        };

    private static string ShortHash(string endpoint)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(endpoint));
        return Convert.ToHexString(bytes)[..12].ToLowerInvariant();
    }

    private readonly record struct Attempt(OutboundResult Result, bool VapidRejected);
}
