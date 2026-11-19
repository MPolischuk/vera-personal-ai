using System.Text.Json;
using Assistant.Abstractions.Tools;
using Assistant.Core.Memory;
using Assistant.Core.Models;
using Assistant.Core.Notes;
using Assistant.Core.Research;
using Microsoft.Extensions.Logging;

namespace Assistant.Extensions.Notes.Tools;

internal sealed class CreateNoteTool(
    INoteStore notes,
    IResearchReadStore reads,
    IMemoryEmbedder embeddings,
    ILogger<CreateNoteTool> logger) : ITool
{
    private static readonly JsonElement Schema = JsonDocument
        .Parse(
            """
            {
              "type": "object",
              "properties": {
                "title": {
                  "type": "string",
                  "description": "Título de la nota."
                },
                "body": {
                  "type": "string",
                  "description": "Cuerpo en prosa. No reemplaza a las afirmaciones citadas."
                },
                "read_ids": {
                  "type": "array",
                  "items": { "type": "string", "format": "uuid" },
                  "description": "Lecturas propias. Se copian el fragmento y la fecha del servidor. No escribas el fragmento."
                },
                "claim_indexes": {
                  "type": "array",
                  "items": { "type": "integer" },
                  "description": "Índices sobre las afirmaciones de read_ids, en ese orden. Vacío copia todas. Un índice fuera de rango falla."
                }
              },
              "required": ["title", "body", "read_ids"],
              "additionalProperties": false
            }
            """)
        .RootElement.Clone();

    public ToolDescriptor Descriptor { get; } = new(
        "create_note",
        "Anota conocimiento del mundo a partir de lecturas ya hechas. El contenido de una página o de un documento se guarda con create_note y no con remember. "
        + "No escribas el fragmento: pasá read_ids de este usuario. Si claim_indexes viene vacío, se copian todas las afirmaciones de esas lecturas.",
        Schema,
        PermissionLevel.ReversibleAction,
        "notes",
        WritesDurableState: true);

    public async ValueTask<ToolResult> ExecuteAsync(
        ToolArguments args, ToolExecutionContext context, CancellationToken ct)
    {
        if (NoteArguments.HasProperty(args, "fragment"))
        {
            return ToolResult.Failed("create_note no acepta un fragmento. Citá read_ids de una lectura.");
        }

        if (!args.TryGetString("title", out var title) || string.IsNullOrWhiteSpace(title))
        {
            return ToolResult.Failed(
                args.TryGetString("title", out _)
                    ? "El parámetro 'title' no puede estar vacío."
                    : "Falta el parámetro obligatorio 'title'.");
        }

        if (!args.TryGetString("body", out var body) || string.IsNullOrWhiteSpace(body))
        {
            return ToolResult.Failed(
                args.TryGetString("body", out _)
                    ? "El parámetro 'body' no puede estar vacío."
                    : "Falta el parámetro obligatorio 'body'.");
        }

        if (!NoteArguments.TryReadGuids(args, "read_ids", required: true, out var readIds, out var readError))
        {
            return ToolResult.Failed(readError);
        }

        if (readIds.Count == 0)
        {
            return ToolResult.Failed("El parámetro 'read_ids' no puede estar vacío.");
        }

        if (readIds.Distinct().Count() != readIds.Count)
        {
            return ToolResult.Failed("El parámetro 'read_ids' repite una lectura.");
        }

        if (!NoteArguments.TryReadOptionalIndexes(args, out var indexes, out var indexError))
        {
            return ToolResult.Failed(indexError);
        }

        var loaded = new List<ResearchRead>(readIds.Count);
        foreach (var readId in readIds)
        {
            var read = await reads.GetAsync(context.UserId, readId, ct);
            if (read is null)
            {
                return ToolResult.Failed("No encuentro esa lectura para este usuario.");
            }

            loaded.Add(read);
        }

        if (!CitedClaims.TrySelect(loaded, indexes, out var sources, out var selectError))
        {
            return ToolResult.Failed(selectError);
        }

        float[] embedding;
        try
        {
            var generated = await embeddings.EmbedDocumentAsync(
                EmbedText(title.Trim(), body.Trim()),
                EmbeddingCallOptions.For(new ModelCallContext(context.ConversationId, context.TurnId)),
                ct);
            embedding = generated.Vector.ToArray();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            NoteLog.EmbeddingFailed(logger, ex, context.TurnId);
            return ToolResult.Failed("No pude guardar la nota porque falló el embedding.");
        }

        var note = await notes.CreateAsync(
            new NewNote(context.UserId, title.Trim(), body.Trim(), embedding, sources),
            ct);
        return ToolResult.Ok(NoteToolJson.Created(note));
    }

    internal static string EmbedText(string title, string body) => title + "\n" + body;
}
