using System.Text.Json;
using Assistant.Abstractions.Tools;
using Assistant.Core.Memory;
using Assistant.Core.Models;
using Assistant.Core.Notes;
using Microsoft.Extensions.Logging;

namespace Assistant.Extensions.Notes.Tools;

internal sealed class SearchNotesTool(
    INoteStore notes,
    IMemoryEmbedder embeddings,
    ILogger<SearchNotesTool> logger) : ITool
{
    private static readonly JsonElement Schema = JsonDocument
        .Parse(
            """
            {
              "type": "object",
              "properties": {
                "query": {
                  "type": "string",
                  "description": "Texto con el que buscar notas parecidas."
                }
              },
              "required": ["query"],
              "additionalProperties": false
            }
            """)
        .RootElement.Clone();

    public ToolDescriptor Descriptor { get; } = new(
        "search_notes",
        "Busca notas guardadas por similitud. Lo que vino de una página o de un documento está en notas, no en recuerdos: no uses recall ni remember para eso.",
        Schema,
        PermissionLevel.Read,
        "notes",
        OutputTrust.Untrusted);

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

        float[] embedding;
        try
        {
            var generated = await embeddings.EmbedQueryAsync(
                [query.Trim()],
                EmbeddingCallOptions.For(new ModelCallContext(context.ConversationId, context.TurnId)),
                ct);
            embedding = generated[0].Vector.ToArray();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            NoteLog.EmbeddingFailed(logger, ex, context.TurnId);
            return ToolResult.Failed("No pude buscar notas porque falló el embedding.");
        }

        var hits = await notes.SearchAsync(
            new SearchNotesQuery(context.UserId, embedding, NoteDefaults.SearchTopK),
            ct);
        return ToolResult.Ok(NoteToolJson.Search(hits), OutputTrust.Untrusted);
    }
}
