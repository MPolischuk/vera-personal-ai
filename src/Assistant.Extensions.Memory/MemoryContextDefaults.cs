namespace Assistant.Extensions.Memory;

internal static class MemoryContextDefaults
{
    public const int RecentUserMessages = 2;

    public const int MaxPreferenceTokens = 128;

    public const int MaxRelevantMemoryTokens = 256;

    /// <summary>
    /// Always-on nudge so remember/recall fire even when the store is empty (ADR-0015).
    /// Priority sits before character.cue so the cue stays last among always-on fragments.
    /// </summary>
    public const int PolicyPriority = 24;
}
