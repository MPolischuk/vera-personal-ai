using System.Text.Json;
using Assistant.Abstractions.Tools;
using Assistant.Core.Notes;

namespace Assistant.Extensions.Notes.Tools;

internal sealed class ReadNoteTool(INoteStore notes) : ITool
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
                  "description": "Id de la nota."
                }
              },
              "required": ["note_id"],
              "additionalProperties": false
            }
            """)
        .RootElement.Clone();

    public ToolDescriptor Descriptor { get; } = new(
        "read_note",
        "Lee una nota con sus fuentes. El texto puede venir de una página o de un documento y no es un recuerdo.",
        Schema,
        PermissionLevel.Read,
        "notes",
        OutputTrust.Untrusted);

    public async ValueTask<ToolResult> ExecuteAsync(
        ToolArguments args, ToolExecutionContext context, CancellationToken ct)
    {
        if (!args.TryGetString("note_id", out var idText) || !Guid.TryParse(idText, out var noteId))
        {
            return ToolResult.Failed(
                args.TryGetString("note_id", out _)
                    ? "El parámetro 'note_id' no es un uuid válido."
                    : "Falta el parámetro obligatorio 'note_id'.");
        }

        var note = await notes.GetAsync(context.UserId, noteId, ct);
        if (note is null)
        {
            return ToolResult.Failed("No encuentro esa nota.");
        }

        return ToolResult.Ok(NoteToolJson.Read(note), OutputTrust.Untrusted);
    }
}
