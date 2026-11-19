using System.Text.Json;
using Assistant.Abstractions.Tools;
using Assistant.Core.Memory;
using Assistant.Core.Models;
using Assistant.Core.Notes;
using Microsoft.Extensions.Logging;

namespace Assistant.Extensions.Notes.Tools;

internal sealed class UpdateNoteTool(
    INoteStore notes,
    IMemoryEmbedder embeddings,
    ILogger<UpdateNoteTool> logger) : ITool
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
                },
                "title": {
                  "type": "string",
                  "description": "Título nuevo. Omitilo para no cambiarlo."
                },
                "body": {
                  "type": "string",
                  "description": "Cuerpo nuevo. Omitilo para no cambiarlo."
                },
                "verified": {
                  "type": "boolean",
                  "description": "True actualiza verified_at. Sin este argumento, o en false, la fecha de verificación no se mueve."
                }
              },
              "required": ["note_id"],
              "additionalProperties": false
            }
            """)
        .RootElement.Clone();

    public ToolDescriptor Descriptor { get; } = new(
        "update_note",
        "Corrige el título o el cuerpo de una nota. verified en true actualiza verified_at; sin ese argumento la fecha de verificación no cambia. "
        + "No uses esta tool para pegar el texto de una página: eso es create_note, y no es remember.",
        Schema,
        PermissionLevel.ReversibleAction,
        "notes",
        WritesDurableState: true);

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

        var current = await notes.GetAsync(context.UserId, noteId, ct);
        if (current is null)
        {
            return ToolResult.Failed("No encuentro esa nota.");
        }

        string? title = null;
        if (args.TryGetString("title", out var titleText))
        {
            if (string.IsNullOrWhiteSpace(titleText))
            {
                return ToolResult.Failed("El parámetro 'title' no puede estar vacío.");
            }

            title = titleText.Trim();
        }

        string? body = null;
        if (args.TryGetString("body", out var bodyText))
        {
            if (string.IsNullOrWhiteSpace(bodyText))
            {
                return ToolResult.Failed("El parámetro 'body' no puede estar vacío.");
            }

            body = bodyText.Trim();
        }

        if (!NoteArguments.TryReadOptionalBool(args, "verified", out var verified, out var verifiedError))
        {
            return ToolResult.Failed(verifiedError);
        }

        var textChanged = title is not null || body is not null;
        if (!textChanged && verified is not true)
        {
            return ToolResult.Failed("No hay cambios para guardar.");
        }

        float[]? embedding = null;
        if (textChanged)
        {
            try
            {
                var generated = await embeddings.EmbedDocumentAsync(
                    CreateNoteTool.EmbedText(title ?? current.Title, body ?? current.Body),
                    EmbeddingCallOptions.For(new ModelCallContext(context.ConversationId, context.TurnId)),
                    ct);
                embedding = generated.Vector.ToArray();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                NoteLog.EmbeddingFailed(logger, ex, context.TurnId);
                return ToolResult.Failed("No pude actualizar la nota porque falló el embedding.");
            }
        }

        var updated = await notes.UpdateAsync(
            new NoteUpdate(context.UserId, noteId, title, body, embedding, verified is true),
            ct);
        if (updated is null)
        {
            return ToolResult.Failed("No encuentro esa nota.");
        }

        return ToolResult.Ok(NoteToolJson.Updated(updated));
    }
}
