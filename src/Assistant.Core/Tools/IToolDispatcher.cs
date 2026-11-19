using Assistant.Abstractions.Tools;
using Assistant.Core.Permissions;

namespace Assistant.Core.Tools;

public interface IToolDispatcher
{
    public ValueTask<ToolInvocationResult> InvokeAsync(
        string name, ToolArguments args, ToolExecutionContext context, CancellationToken ct);
}

public sealed record ToolInvocationResult(
    PermissionOutcome Permission,
    ToolResult? Result,
    string? Reason,
    string? ConfirmationPrompt,
    TimeSpan Duration);
