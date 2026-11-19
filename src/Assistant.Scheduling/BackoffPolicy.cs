namespace Assistant.Scheduling;

/// <summary>
/// Standard retry delays: 15s, 1min, 5min, 15min, then the attempt budget is exhausted.
/// </summary>
internal static class BackoffPolicy
{
    private static readonly TimeSpan[] Steps =
    [
        TimeSpan.FromSeconds(15),
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
    ];

    /// <summary>
    /// Delay before the next attempt. Null means the job is terminally failed.
    /// </summary>
    /// <param name="attempt">1-based attempt that just failed (the claim already counted it).</param>
    /// <param name="maxAttempts">Attempt budget.</param>
    /// <param name="after">Caller override. Null uses the standard step.</param>
    public static TimeSpan? DelayAfterAttempt(int attempt, int maxAttempts, TimeSpan? after)
    {
        if (attempt >= maxAttempts)
        {
            return null;
        }

        if (after is { } requested)
        {
            return requested < TimeSpan.Zero ? TimeSpan.Zero : requested;
        }

        var index = attempt - 1;
        if (index < 0)
        {
            index = 0;
        }

        return index >= Steps.Length ? Steps[^1] : Steps[index];
    }
}
