using System.Text.Json;
using Assistant.Abstractions.Tools;
using Microsoft.Extensions.Logging;

namespace Assistant.Extensions.Memory.Tools;

internal sealed class RecallTool(MemoryRecorder recorder, ILogger<RecallTool> logger) : ITool
{
    private static readonly JsonElement Schema = JsonDocument
        .Parse(
            """
            {
              "type": "object",
              "properties": {
                "query": {
                  "type": "string",
                  "description": "Consulta en lenguaje natural para recuperar recuerdos."
                }
              },
              "required": ["query"],
              "additionalProperties": false
            }
            """)
        .RootElement.Clone();

    public ToolDescriptor Descriptor { get; } = new(
        "recall",
        "Cuando el usuario pregunta un dato suyo ya dicho (dónde vive, cómo se llama, trabajo, gustos), llamá recall con esa consulta antes de decir que no lo tenés. "
        + "Usá sólo lo que devuelva la tool. Lista vacía: decí que no hay recuerdo, no inventes. "
        + "No inventes memory_id ni recites el almacén.",
        Schema,
        PermissionLevel.Read,
        "memory");

    public async ValueTask<ToolResult> ExecuteAsync(
        ToolArguments args, ToolExecutionContext context, CancellationToken ct)
    {
        if (!args.TryGetString("query", out var query) || string.IsNullOrWhiteSpace(query))
        {
            return ToolResult.Failed(
                args.TryGetString("query", out _)
                    ? "El parámetro 'query' no puede estar vacío."
                    : "Falta el parámetro obligatorio 'query'.");
        }

        try
        {
            var hits = await recorder.SearchAsync(
                query,
                kind: null,
                since: null,
                context,
                MemoryToolDefaults.RecallTopK,
                MemoryToolDefaults.RecallMinScore,
                ct);
            return ToolResult.Ok(MemoryToolJson.Hits(hits));
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            MemoryLog.EmbedFailed(logger, e, context.TurnId);
            return ToolResult.Failed("No pude buscar recuerdos porque falló el embedding.");
        }
    }
}
