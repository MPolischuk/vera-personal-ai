namespace Assistant.Api.Speech;

/// <summary>
/// Assembles one user utterance from Deepgram listen events.
/// Each <c>is_final</c> is a new span of audio (a pause can split a sentence);
/// the client VAD decides when the stream actually ends.
/// </summary>
internal static class SttUtterance
{
    public static string Join(IEnumerable<string> finals, string? lastInterim)
    {
        var parts = new List<string>();
        foreach (var raw in finals)
        {
            var trimmed = raw.Trim();
            if (trimmed.Length > 0)
            {
                parts.Add(trimmed);
            }
        }

        if (parts.Count > 0)
        {
            return string.Join(" ", parts);
        }

        return lastInterim?.Trim() ?? string.Empty;
    }
}
