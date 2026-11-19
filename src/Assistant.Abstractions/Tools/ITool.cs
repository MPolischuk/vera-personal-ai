using System.Text.Json;

namespace Assistant.Abstractions.Tools;

/// <summary>
/// Seam 2: an action capability enters here. The implementation is <b>never</b>
/// invoked directly: the only path is the dispatcher, which checks permissions first
/// (ADR-0009, rule 1).
/// </summary>
public interface ITool
{
    public ToolDescriptor Descriptor { get; }

    public ValueTask<ToolResult> ExecuteAsync(
        ToolArguments args, ToolExecutionContext context, CancellationToken ct);
}

/// <summary>
/// Receives catalog names after the catalog has stored every tool.
/// The descriptor may then mention only the names that exist.
/// </summary>
public interface IBindsToolNames
{
    public void Bind(Func<IReadOnlyCollection<string>> toolNames);
}

public sealed record ToolDescriptor(
    string Name,
    string Description,
    JsonElement ParametersSchema,
    PermissionLevel Level,
    string ExtensionName,
    OutputTrust OutputTrust = OutputTrust.Trusted,
    bool WritesDurableState = false);

/// <summary>
/// Order matters and so does the zero value: undeclared is denied
/// (ADR-0009, rule 3; see PT-03 D10).
/// </summary>
public enum PermissionLevel
{
    Undeclared = 0,
    Read = 1,
    ReversibleAction = 2,
    SensitiveAction = 3,
}

/// <summary>
/// Provenance of a tool result (ADR-0018). Zero is <see cref="Untrusted"/>.
/// The record parameter defaults to <see cref="Trusted"/> so in-process tools
/// stay trusted; MCP projection always passes the manifest policy.
/// </summary>
public enum OutputTrust
{
    Untrusted = 0,
    Trusted = 1,
}

public sealed record ToolExecutionContext(
    Guid UserId,
    Guid ConversationId,
    Guid TurnId,
    Guid? ConfirmationId = null,
    bool TurnContaminated = false,
    string? ToolCallId = null);
