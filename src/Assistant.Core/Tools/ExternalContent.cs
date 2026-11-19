namespace Assistant.Core.Tools;

/// <summary>
/// Public fence for untrusted text that enters a turn from outside a tool (ADR-0022).
/// Uses the same markers and body cleaning as tool results. The body is not logged.
/// </summary>
public static class ExternalContent
{
    /// <summary>
    /// Wraps <paramref name="body"/> in the untrusted-content fence.
    /// </summary>
    /// <param name="source">Channel or producer (for example, <c>telegram</c>).</param>
    /// <param name="kind">What the text is (for example, <c>forwarded_message</c>).</param>
    /// <param name="origin">Visible origin label. Null becomes an empty attribute.</param>
    /// <param name="body">Raw text. Fence markers and chat-template tokens inside it are neutralized.</param>
    public static string Wrap(string source, string kind, string? origin, string? body)
        => UntrustedContentEnvelope.Wrap(source, kind, origin, body);
}
