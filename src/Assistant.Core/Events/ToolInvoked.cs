using Assistant.Abstractions.Events;
using Assistant.Abstractions.Tools;
using Assistant.Core.Permissions;

namespace Assistant.Core.Events;

public sealed record ToolInvoked(
    ToolExecutionContext Context,
    string ToolName,
    string ExtensionName,
    PermissionLevel Level,
    PermissionOutcome Outcome,
    string? Reason,
    string RawArguments,
    ToolResult? Result,
    TimeSpan Duration) : IDomainEvent;
