using System.Text.Json;
using Assistant.Abstractions.Tools;
using Assistant.Core.Memory;
using Microsoft.Extensions.Logging;

namespace Assistant.Extensions.Memory.Tools;

internal sealed class RememberTool(MemoryRecorder recorder, ILogger<RememberTool> logger) : ITool
{
    private static readonly JsonElement Schema = JsonDocument
        .Parse(
            """
            {
              "type": "object",
              "properties": {
                "content": {
                  "type": "string",
                  "description": "Texto del recuerdo a guardar, en prosa."
                },
                "kind": {
                  "type": "string",
                  "enum": ["fact", "episode", "context", "task"],
                  "description": "Tipo: fact, episode, context o task."
                },
                "due_at": {
                  "type": "string",
                  "format": "date-time",
                  "description": "Fecha ISO opcional de un pendiente (kind task)."
                },
                "pinned": {
                  "type": "boolean",
                  "description": "True para nombre, dónde vive, lugar de trabajo, zona horaria, idioma o gente cercana. Un gusto, un episodio o una tarea no se fija."
                },
                "replaces_id": {
                  "type": "string",
                  "format": "uuid",
                  "description": "Id del recuerdo anterior en una mudanza o un cambio de trabajo. Lo borra en lógico y crea la fila nueva."
                },
                "index_terms": {
                  "type": "string",
                  "description": "Palabras opcionales para buscar este recuerdo (obra, género, lugar). No forman parte del hecho. Si no aportan, omitilas."
                }
              },
              "required": ["content", "kind"],
              "additionalProperties": false
            }
            """)
        .RootElement.Clone();

    public ToolDescriptor Descriptor { get; } = new(
        "remember",
        "Cuando el usuario afirma un hecho estable suyo (nombre, dónde vive, fecha de nacimiento, trabajo, un gusto durable, gente importante), pide que te acuerdes, o deja un pendiente con fecha, llamá remember en ese turno. "
        + "Nombre, dónde vive, lugar de trabajo, zona horaria, idioma y gente cercana van con pinned: true. Un gusto, un episodio o una tarea no se fija. "
        + "Una mudanza o un cambio de trabajo lleva replaces_id del hecho anterior. "
        + "Varios hechos en un mensaje: varias llamadas o un solo content que los liste. "
        + "Decir que quedó anotado, repetir la lista o «acordate que sos…» sin la tool no guarda nada. "
        + "No la uses para saludos, el clima del día, cháchara sin dato durable, gustos de Vera, "
        + "emociones inferidas ni secretos de terceros. Un hábito o setting con clave estable es update_preference, no recuerdo.",
        Schema,
        PermissionLevel.ReversibleAction,
        "memory",
        WritesDurableState: true);

    public async ValueTask<ToolResult> ExecuteAsync(
        ToolArguments args, ToolExecutionContext context, CancellationToken ct)
    {
        if (!args.TryGetString("content", out var content) || string.IsNullOrWhiteSpace(content))
        {
            return ToolResult.Failed(
                args.TryGetString("content", out _)
                    ? "El parámetro 'content' no puede estar vacío."
                    : "Falta el parámetro obligatorio 'content'.");
        }

        if (!args.TryGetString("kind", out var kindText)
            || !Enum.TryParse<MemoryKind>(kindText, ignoreCase: true, out var kind)
            || !Enum.IsDefined(kind))
        {
            return ToolResult.Failed("El parámetro 'kind' debe ser fact, episode, context o task.");
        }

        DateTimeOffset? dueAt = null;
        if (args.TryGetString("due_at", out var dueText))
        {
            if (!DateTimeOffset.TryParse(dueText, out var parsed))
            {
                return ToolResult.Failed("El parámetro 'due_at' no es una fecha ISO válida.");
            }

            dueAt = parsed;
        }

        if (!TryReadOptionalBool(args, out var pinned, out var pinnedError))
        {
            return ToolResult.Failed(pinnedError);
        }

        Guid? replacesId = null;
        if (args.TryGetString("replaces_id", out var replacesText))
        {
            if (!Guid.TryParse(replacesText, out var parsedId))
            {
                return ToolResult.Failed("El parámetro 'replaces_id' no es un uuid válido.");
            }

            replacesId = parsedId;
        }

        if (!TryReadOptionalString(args, "index_terms", out var indexTerms, out var termsError))
        {
            return ToolResult.Failed(termsError);
        }

        try
        {
            var remembered = await recorder.RememberAsync(
                content, kind, dueAt, pinned, replacesId, indexTerms, context, ct);
            return remembered switch
            {
                RememberedMemory stored => ToolResult.Ok(MemoryToolJson.Remember(stored.Item, stored.Updated)),
                RememberRejected rejected => ToolResult.Failed(rejected.Error),
                _ => ToolResult.Failed("No pude guardar el recuerdo."),
            };
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            MemoryLog.EmbedFailed(logger, e, context.TurnId);
            return ToolResult.Failed("No pude guardar el recuerdo porque falló el embedding.");
        }
    }

    private static bool TryReadOptionalBool(ToolArguments args, out bool? value, out string error)
    {
        using var document = JsonDocument.Parse(args.RawJson);
        if (document.RootElement.ValueKind is not JsonValueKind.Object
            || !document.RootElement.TryGetProperty("pinned", out var property))
        {
            value = null;
            error = "";
            return true;
        }

        if (property.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            value = property.GetBoolean();
            error = "";
            return true;
        }

        value = null;
        error = "El parámetro 'pinned' tiene que ser true o false.";
        return false;
    }

    private static bool TryReadOptionalString(
        ToolArguments args, string name, out string? value, out string error)
    {
        using var document = JsonDocument.Parse(args.RawJson);
        if (document.RootElement.ValueKind is not JsonValueKind.Object
            || !document.RootElement.TryGetProperty(name, out var property))
        {
            value = null;
            error = "";
            return true;
        }

        if (property.ValueKind is JsonValueKind.String)
        {
            value = property.GetString();
            error = "";
            return true;
        }

        value = null;
        error = "El parámetro 'index_terms' tiene que ser texto.";
        return false;
    }
}
