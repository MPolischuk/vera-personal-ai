using Assistant.Abstractions.Tools;

namespace Assistant.Core.Tools;

/// <summary>
/// ADR-0018 rule 2: the dispatcher wraps untrusted tool text. The fence is code,
/// not a prompt. The body is not logged.
/// </summary>
internal static class UntrustedContentEnvelope
{
    private const string StartMarker = "<<<untrusted_content";
    private const string EndMarker = "<<<end_untrusted_content>>>";

    private static readonly string[] TemplateTokens =
    [
        "<|im_start|>",
        "<|im_end|>",
        "<|im_sep|>",
        "<|endoftext|>",
    ];

    public static ToolResult Apply(ToolDescriptor descriptor, ToolResult result)
        => result.Status switch
        {
            ToolStatus.Ok => result with { Content = Wrap(descriptor, result.Content) },
            ToolStatus.Failed => result with { Error = Wrap(descriptor, result.Error) },
            _ => throw new ArgumentOutOfRangeException(nameof(result), result.Status, "The tool status is not Ok or Failed."),
        };

    public static bool ContainsMarker(string? text)
        => text?.Contains(StartMarker, StringComparison.Ordinal) == true;

    /// <summary>
    /// Same fence and body cleaning as tool results. Attributes differ: external
    /// text names a source, a kind, and an origin instead of a server and a tool.
    /// </summary>
    internal static string Wrap(string source, string kind, string? origin, string? body)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(kind);

        var attributes =
            $"source=\"{SanitizeAttribute(source)}\" kind=\"{SanitizeAttribute(kind)}\" origin=\"{SanitizeAttribute(origin ?? "")}\"";
        return Format(attributes, body);
    }

    private static string Wrap(ToolDescriptor descriptor, string? body)
    {
        var server = SanitizeAttribute(descriptor.ExtensionName);
        var tool = SanitizeAttribute(descriptor.Name);
        return Format($"server=\"{server}\" tool=\"{tool}\"", body);
    }

    private static string Format(string attributes, string? body)
    {
        var cleaned = Clean(body ?? "");
        return $"<<<untrusted_content {attributes}>>>\n{cleaned}\n{EndMarker}";
    }

    private static string Clean(string body)
    {
        foreach (var token in TemplateTokens)
        {
            body = body.Replace(token, "", StringComparison.Ordinal);
        }

        // Insert a space after the first '<' so a body cannot close or reopen the fence.
        // The neutralized form does not match ContainsMarker.
        body = body.Replace(StartMarker, "< " + StartMarker[1..], StringComparison.Ordinal);
        body = body.Replace(EndMarker, "< " + EndMarker[1..], StringComparison.Ordinal);
        return body;
    }

    private static string SanitizeAttribute(string value)
        => value.ReplaceLineEndings(" ").Replace("\"", "'", StringComparison.Ordinal);
}
