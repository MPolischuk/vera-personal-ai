using System.Text.Encodings.Web;
using System.Text.Json;
using Assistant.Abstractions.Tools;
using Assistant.Core.Models;
using Assistant.Core.Research;

namespace Assistant.Extensions.Research.Tools;

internal sealed class ReadDocumentTool(
    IDocumentLibrary library,
    IDocumentConverter converter,
    SourceSummarizer summarizer,
    IResearchReadStore store,
    ReadBudget budget) : ITool
{
    internal const string ToolName = "read_document";

    internal const string NotFoundMessage = "No encontré ese documento.";

    internal const string UncitableMessage = "no pude citar este documento";

    internal const string QuotaExhaustedMessage = "se agotó el cupo de lectura";

    private static readonly JsonSerializerOptions OutputJson = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly JsonElement Schema = JsonDocument
        .Parse(
            """
            {
              "type": "object",
              "properties": {
                "id": {
                  "type": "string",
                  "description": "Id del documento que el usuario subió. No es una URL."
                }
              },
              "required": ["id"],
              "additionalProperties": false
            }
            """)
        .RootElement.Clone();

    public ToolDescriptor Descriptor { get; } = new(
        ToolName,
        "Resume un documento ya subido y devuelve un resumen, las afirmaciones con su número de página y un read_id. No envía el archivo crudo ni una URL.",
        Schema,
        PermissionLevel.Read,
        "research",
        OutputTrust.Untrusted,
        WritesDurableState: false);

    public async ValueTask<ToolResult> ExecuteAsync(
        ToolArguments args, ToolExecutionContext context, CancellationToken ct)
    {
        if (!args.TryGetString("id", out var rawId) || string.IsNullOrWhiteSpace(rawId))
        {
            return ToolResult.Failed(
                args.TryGetString("id", out _)
                    ? "El parámetro 'id' no puede estar vacío."
                    : "Falta el parámetro obligatorio 'id'.");
        }

        if (!Guid.TryParse(rawId.Trim(), out var documentId))
        {
            return ToolResult.Failed("El parámetro 'id' no es un id de documento.");
        }

        var stored = await library.OpenAsync(context.UserId, documentId, ct).ConfigureAwait(false);
        if (stored is null)
        {
            return ToolResult.Failed(NotFoundMessage);
        }

        if (!await budget.TryConsumeAsync(context.UserId, context.TurnId, ct).ConfigureAwait(false))
        {
            return ToolResult.Failed(QuotaExhaustedMessage);
        }

        IReadOnlyList<DocumentChunk> chunks;
        SourceSummary? summary;
        try
        {
            chunks = await converter.ConvertAsync(stored.Content, stored.MediaType, ct).ConfigureAwait(false);
            if (chunks.Count == 0)
            {
                return ToolResult.Failed(UncitableMessage);
            }

            summary = await summarizer.SummarizeChunksAsync(
                chunks,
                new ModelCallContext(context.ConversationId, context.TurnId),
                ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return ToolResult.Failed(UncitableMessage);
        }

        if (summary is null || summary.Claims.Count == 0)
        {
            return ToolResult.Failed(UncitableMessage);
        }

        var claims = summary.Claims
            .Select(claim => WithProvenance(claim, chunks, documentId))
            .ToArray();
        var saved = await store.AddAsync(
            new NewResearchRead(
                context.UserId,
                context.ConversationId,
                context.TurnId,
                summary.Summary,
                claims,
                SourceUrl: ""),
            ct).ConfigureAwait(false);

        return ToolResult.Ok(Present(saved), OutputTrust.Untrusted);
    }

    private static ResearchClaim WithProvenance(
        ResearchClaim claim,
        IReadOnlyList<DocumentChunk> chunks,
        Guid documentId)
    {
        var match = chunks.FirstOrDefault(chunk =>
            chunk.Text.Contains(claim.Fragment, StringComparison.Ordinal)) ?? chunks[0];
        return claim with
        {
            Page = match.Page,
            Bbox = match.Bbox,
            DocumentId = documentId,
        };
    }

    private static string Present(ResearchRead read)
        => JsonSerializer.Serialize(
            new
            {
                read_id = read.Id,
                summary = read.Summary,
                claims = read.Claims.Select(claim => new
                {
                    text = claim.Text,
                    fragment = claim.Fragment,
                    page = claim.Page,
                }).ToArray(),
            },
            OutputJson);
}
