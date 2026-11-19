namespace Assistant.Channels.Telegram.Api;

/// <summary>
/// Applies the per-call timeout from <see cref="TimeoutKey"/>. A timeout becomes
/// <see cref="TimeoutException"/> so the resilience handler can retry it; caller cancellation does not.
/// </summary>
public sealed class TelegramTimeoutHandler : DelegatingHandler
{
    public static readonly HttpRequestOptionsKey<TimeSpan> TimeoutKey = new("Telegram.Timeout");

    public static readonly TimeSpan MethodTimeout = TimeSpan.FromSeconds(10);

    public static readonly TimeSpan DownloadTimeout = TimeSpan.FromSeconds(30);

    public static readonly TimeSpan GetUpdatesTimeout = TimeSpan.FromSeconds(40);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var timeout = request.Options.TryGetValue(TimeoutKey, out var configured)
            ? configured
            : MethodTimeout;

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            return await base.SendAsync(request, timeoutSource.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Telegram request timed out.");
        }
    }
}
