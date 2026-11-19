using System.Text.Json;
using Assistant.Abstractions.Tools;
using Assistant.Core.Memory;

namespace Assistant.Extensions.Memory.Tools;

internal sealed class GetPreferencesTool(IPreferenceStore store) : ITool
{
    private static readonly JsonElement Schema = JsonDocument
        .Parse(
            """
            {
              "type": "object",
              "properties": {},
              "additionalProperties": false
            }
            """)
        .RootElement.Clone();

    public ToolDescriptor Descriptor { get; } = new(
        "get_preferences",
        "Lista las preferencias del usuario (clave y valor). Llamala cuando pregunte qué preferencias tiene o un hábito con clave. "
        + "No recites el almacén: usalas. "
        + "Un hábito o setting con clave estable se cambia con update_preference; prosa es remember. "
        + "No las uses para gustos de Vera.",
        Schema,
        PermissionLevel.Read,
        "memory");

    public async ValueTask<ToolResult> ExecuteAsync(
        ToolArguments args, ToolExecutionContext context, CancellationToken ct)
    {
        var items = await store.ListAsync(context.UserId, ct);
        return ToolResult.Ok(MemoryToolJson.Preferences(items));
    }
}
