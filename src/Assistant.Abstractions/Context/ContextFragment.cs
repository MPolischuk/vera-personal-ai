namespace Assistant.Abstractions.Context;

public sealed record ContextFragment(
    string Name,
    ContextPlacement Placement,
    IReadOnlyList<string> Parts,
    FragmentTrim Trim = FragmentTrim.Never,
    int EstimatedTokens = 0)
{
    /// <summary>
    /// Stamped by the assembler from the contributor. Whatever a contributor returns
    /// here is discarded: priority is declared on the interface, not per turn (D6).
    /// </summary>
    public int Priority { get; init; }

    public string Content => string.Join("\n", Parts);

    public static ContextFragment Text(
        string name, ContextPlacement placement, string content, int estimatedTokens = 0)
        => new(name, placement, [content], FragmentTrim.Never, estimatedTokens);
}

public enum ContextPlacement
{
    SystemPrompt,
    PrependedMessage,
    AppendedMessage,

    /// <summary>
    /// The fragment is prior conversation: one part per message, in the same order
    /// as <see cref="ContextRequest.RecentHistory"/>. The orchestrator maps them to chat
    /// messages with roles; other values travel as text (PT-06 D5).
    /// </summary>
    ConversationHistory,
}

/// <summary>
/// What to do when the fragment does not fit the budget. <c>FromStart</c> drops parts
/// from the old end before dropping the whole fragment.
/// </summary>
public enum FragmentTrim { Never, FromStart }
