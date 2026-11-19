using Assistant.Abstractions.Tools;

namespace Assistant.Core.Permissions;

public interface IPermissionEngine
{
    public ValueTask<PermissionDecision> EvaluateAsync(
        ToolDescriptor tool, ToolArguments args, ToolExecutionContext context, CancellationToken ct);
}

public sealed record PermissionDecision(
    PermissionOutcome Outcome,
    string? Reason,
    string? ConfirmationPrompt)
{
    public static PermissionDecision Allow() => new(PermissionOutcome.Allow, null, null);

    public static PermissionDecision Deny(string reason) => new(PermissionOutcome.Deny, reason, null);

    public static PermissionDecision Confirm(string prompt)
        => new(PermissionOutcome.RequireConfirmation, null, prompt);
}

public enum PermissionOutcome { Allow, RequireConfirmation, Deny }
