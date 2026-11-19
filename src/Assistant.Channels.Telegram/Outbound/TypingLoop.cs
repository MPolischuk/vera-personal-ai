namespace Assistant.Channels.Telegram.Outbound;

/// <summary>
/// Repeats a typing action every <see cref="Interval"/> while a turn is running.
/// The first action is sent by the sink; this loop waits one interval before the next.
/// </summary>
internal static class TypingLoop
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(4);

    public static async Task RunAsync(
        Func<CancellationToken, ValueTask> beat,
        TimeSpan interval,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(beat);
        try
        {
            using var timer = new PeriodicTimer(interval);
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                await beat(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The turn finished. Typing stops with it.
        }
    }
}
