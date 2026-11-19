using System.Text.Json;
using Assistant.Abstractions.Tasks;
using Assistant.Abstractions.Tools;

namespace Assistant.Extensions.Delegation.Tools;

internal sealed class ListTasksTool(IAgentTaskStore store) : ITool
{
    private static readonly JsonElement Schema = JsonDocument.Parse(
        """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {}
        }
        """).RootElement.Clone();

    public ToolDescriptor Descriptor { get; } = new(
        "list_tasks",
        "Lista las tareas del usuario: id, título, tipo, estado, motivo, costo y fechas. "
        + "No incluye la instrucción ni el resultado.",
        Schema,
        PermissionLevel.Read,
        "delegation");

    public async ValueTask<ToolResult> ExecuteAsync(
        ToolArguments args, ToolExecutionContext context, CancellationToken ct)
    {
        var items = await store.ListForUserAsync(context.UserId, ct);
        return ToolResult.Ok(DelegationToolJson.Listed(items));
    }
}
