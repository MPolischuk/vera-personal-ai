namespace Assistant.Abstractions.Tools;

/// <summary>
/// Optional card text for a tool that needs confirmation. The dispatcher calls
/// <see cref="DescribeAsync"/> when the permission outcome is confirmation,
/// before that prompt is persisted. A failure does not create a confirmation:
/// the same error the tool would return on execute is handed back instead.
/// </summary>
public interface IToolConfirmationDetail
{
    public ValueTask<ToolConfirmationText> DescribeAsync(
        ToolArguments args, ToolExecutionContext context, CancellationToken ct);
}

/// <summary>
/// Either the text shown on the confirmation card, or the tool error that
/// replaces the card when the action cannot be described.
/// </summary>
public sealed record ToolConfirmationText(string? Prompt, string? Error)
{
    public static ToolConfirmationText Show(string prompt) => new(prompt, null);

    public static ToolConfirmationText Failed(string error) => new(null, error);
}
