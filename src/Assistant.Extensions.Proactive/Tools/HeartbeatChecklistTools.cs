using System.Text.Json;
using Assistant.Abstractions.Proactive;
using Assistant.Abstractions.Tools;

namespace Assistant.Extensions.Proactive.Tools;

internal sealed class SetHeartbeatChecklistTool(IHeartbeatChecklistStore checks) : ITool
{
    private static readonly JsonElement Schema = JsonDocument.Parse(
        """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["text"],
          "properties": {
            "text": {
              "type": "string",
              "maxLength": 2000,
              "description": "Qué tiene que mirar el heartbeat. Un texto vacío hace que no llame al modelo."
            }
          }
        }
        """).RootElement.Clone();

    public ToolDescriptor Descriptor { get; } = new(
        "set_heartbeat_checklist",
        "Reemplaza la lista de revisión del heartbeat. Un texto vacío lo deja en silencio.",
        Schema,
        PermissionLevel.ReversibleAction,
        "proactive",
        WritesDurableState: true);

    public async ValueTask<ToolResult> ExecuteAsync(
        ToolArguments args, ToolExecutionContext context, CancellationToken ct)
    {
        if (!args.TryGetString("text", out var text))
        {
            return ToolResult.Failed("Falta el texto de la lista.");
        }

        try
        {
            await checks.SetAsync(context.UserId, text, ct);
            return ToolResult.Ok("ok");
        }
        catch (ArgumentException)
        {
            return ToolResult.Failed("La lista tiene que tener como máximo 2000 caracteres.");
        }
    }
}

internal sealed class ListHeartbeatChecklistTool(IHeartbeatChecklistStore checks) : ITool
{
    private static readonly JsonElement Schema = JsonDocument.Parse(
        """
        { "type": "object", "additionalProperties": false, "properties": {} }
        """).RootElement.Clone();

    public ToolDescriptor Descriptor { get; } = new(
        "list_heartbeat_checklist",
        "Devuelve la lista de revisión del heartbeat. Si todavía no hay una, devuelve la lista por defecto.",
        Schema,
        PermissionLevel.Read,
        "proactive");

    public async ValueTask<ToolResult> ExecuteAsync(
        ToolArguments args, ToolExecutionContext context, CancellationToken ct)
    {
        var stored = await checks.GetAsync(context.UserId, ct);
        return ToolResult.Ok(stored ?? HeartbeatJobs.DefaultChecklist);
    }
}
