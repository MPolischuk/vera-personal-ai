namespace Assistant.Channels.Inbox;

/// <summary><c>Channels</c> section fields the inbox reads. Other keys in the section are ignored.</summary>
public sealed class InboxOptions
{
    public const string SectionName = "Channels";

    public int InboxParallelism { get; set; } = 4;

    /// <summary>How long a dedup key is kept after it arrives. Payload is already gone.</summary>
    public Dictionary<string, TimeSpan> InboxRetention { get; set; } = new(StringComparer.Ordinal)
    {
        ["telegram"] = TimeSpan.FromDays(3),
        ["whatsapp"] = TimeSpan.FromDays(8),
    };
}
