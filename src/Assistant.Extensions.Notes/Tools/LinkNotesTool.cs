using System.Text.Json;
using Assistant.Abstractions.Tools;
using Assistant.Core.Notes;

namespace Assistant.Extensions.Notes.Tools;

internal sealed class LinkNotesTool(INoteStore notes) : ITool
{
    private static readonly JsonElement Schema = JsonDocument
        .Parse(
            """
            {
              "type": "object",
              "properties": {
                "note_id": {
                  "type": "string",
                  "format": "uuid",
                  "description": "Id de una nota."
                },
                "other_note_id": {
                  "type": "string",
                  "format": "uuid",
                  "description": "Id de la otra nota. Se escriben los dos sentidos."
                }
              },
              "required": ["note_id", "other_note_id"],
              "additionalProperties": false
            }
            """)
        .RootElement.Clone();

    public ToolDescriptor Descriptor { get; } = new(
        "link_notes",
        "Enlaza dos notas del mismo usuario. Escribe los dos sentidos o ninguno. No guarda texto de una fuente ni un recuerdo.",
        Schema,
        PermissionLevel.ReversibleAction,
        "notes",
        WritesDurableState: true);

    public async ValueTask<ToolResult> ExecuteAsync(
        ToolArguments args, ToolExecutionContext context, CancellationToken ct)
    {
        if (!TryReadId(args, "note_id", out var noteId, out var noteError))
        {
            return ToolResult.Failed(noteError);
        }

        if (!TryReadId(args, "other_note_id", out var otherNoteId, out var otherError))
        {
            return ToolResult.Failed(otherError);
        }

        var status = await notes.LinkAsync(context.UserId, noteId, otherNoteId, ct);
        return status switch
        {
            NoteLinkStatus.Linked => ToolResult.Ok(NoteToolJson.Linked(noteId, otherNoteId)),
            NoteLinkStatus.SameNote => ToolResult.Failed("Una nota no se enlaza consigo misma."),
            _ => ToolResult.Failed("No encuentro esas notas para este usuario."),
        };
    }

    private static bool TryReadId(ToolArguments args, string name, out Guid id, out string error)
    {
        if (!args.TryGetString(name, out var text))
        {
            id = default;
            error = $"Falta el parámetro obligatorio '{name}'.";
            return false;
        }

        if (!Guid.TryParse(text, out id))
        {
            error = $"El parámetro '{name}' no es un uuid válido.";
            return false;
        }

        error = "";
        return true;
    }
}
