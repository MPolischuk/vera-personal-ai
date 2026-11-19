using System.Collections.Concurrent;
using System.Diagnostics;

namespace Assistant.Channels.WebPush;

/// <summary>
/// Stashes <c>Retry-After</c> before the push library disposes the response.
/// A static map is used because <see cref="HttpClient"/> restores the execution context
/// after the handler returns, which drops an <see cref="AsyncLocal{T}"/> set inside it.
/// </summary>
internal static class WebPushRetryAfterHint
{
    private static readonly ConcurrentDictionary<string, TimeSpan> Pending = new(StringComparer.Ordinal);

    public static void Remember(Uri? endpoint, TimeSpan? delay)
    {
        if (endpoint is null)
        {
            return;
        }

        if (delay is { Ticks: > 0 } value)
        {
            Pending[endpoint.AbsoluteUri] = value;
            return;
        }

        Pending.TryRemove(endpoint.AbsoluteUri, out _);
    }

    public static TimeSpan? Take(string endpoint)
    {
        if (!Pending.TryRemove(endpoint, out var delay) && Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
        {
            Pending.TryRemove(uri.AbsoluteUri, out delay);
        }

        return delay is { Ticks: > 0 } value ? value : null;
    }
}

internal static class WebPushActivityRedaction
{
    public static void RedactCurrent()
    {
        if (Activity.Current is not { } activity)
        {
            return;
        }

        foreach (var tag in activity.TagObjects.ToArray())
        {
            if (tag.Value is string text && Uri.TryCreate(text, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host))
            {
                activity.SetTag(tag.Key, uri.GetLeftPart(UriPartial.Authority) + "/");
            }
        }

        if (Uri.TryCreate(activity.DisplayName, UriKind.Absolute, out var display) && !string.IsNullOrEmpty(display.Host))
        {
            activity.DisplayName = display.GetLeftPart(UriPartial.Authority) + "/";
        }
    }
}

internal sealed class WebPushOutboundHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        WebPushActivityRedaction.RedactCurrent();
        try
        {
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if ((int)response.StatusCode == 429)
            {
                WebPushRetryAfterHint.Remember(request.RequestUri, ReadRetryAfter(response));
            }

            return response;
        }
        finally
        {
            WebPushActivityRedaction.RedactCurrent();
        }
    }

    private static TimeSpan? ReadRetryAfter(HttpResponseMessage response)
    {
        var retry = response.Headers.RetryAfter;
        if (retry is null)
        {
            return null;
        }

        if (retry.Delta is { } delta)
        {
            return delta;
        }

        if (retry.Date is { } date)
        {
            var delay = date - DateTimeOffset.UtcNow;
            return delay > TimeSpan.Zero ? delay : TimeSpan.Zero;
        }

        return null;
    }
}
