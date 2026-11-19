using System.Text.Json;
using Assistant.Abstractions.Tools;

namespace Assistant.Api.Tools;

/// <summary>
/// Exists only to exercise the stage 1a confirmation flow. Removed when V8
/// brings real SensitiveAction tools.
/// </summary>
internal sealed class EchoWithConfirmationTool : ITool
{
    private static readonly JsonElement Schema = JsonDocument
        .Parse(
            """
            {
              "type": "object",
              "properties": {
                "text": {
                  "type": "string",
                  "description": "Texto a devolver tras la confirmación."
                }
              },
              "required": ["text"],
              "additionalProperties": false
            }
            """)
        .RootElement.Clone();

    public ToolDescriptor Descriptor { get; } = new(
        "echo_with_confirmation",
        "Devuelve el texto recibido. Requiere confirmación del usuario antes de ejecutarse. "
        + "Usala sólo cuando el usuario pida explícitamente repetir algo con confirmación.",
        Schema,
        PermissionLevel.SensitiveAction,
        "builtin");

    public ValueTask<ToolResult> ExecuteAsync(
        ToolArguments args, ToolExecutionContext context, CancellationToken ct)
    {
        if (!args.TryGetString("text", out var text))
        {
            return ValueTask.FromResult(ToolResult.Failed("Falta el parámetro obligatorio 'text'."));
        }

        return ValueTask.FromResult(ToolResult.Ok(text));
    }
}
