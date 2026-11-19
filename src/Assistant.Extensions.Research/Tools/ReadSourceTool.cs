using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Assistant.Abstractions.Tools;
using Assistant.Core.Models;
using Assistant.Core.Research;

namespace Assistant.Extensions.Research.Tools;

internal sealed class ReadSourceTool(
    PageFetcher fetcher,
    SourceSummarizer summarizer,
    IResearchReadStore store,
    ReadBudget budget) : ITool
{
    internal const string QuotaExhaustedMessage = "se agotó el cupo de lectura";

    internal const string UncitableMessage = "no pude citar esta fuente";

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
                "url": {
                  "type": "string",
                  "description": "URL http o https de la página a leer."
                }
              },
              "required": ["url"],
              "additionalProperties": false
            }
            """)
        .RootElement.Clone();

    public ToolDescriptor Descriptor { get; } = new(
        ReadBudget.ToolName,
        "Lee la URL pública indicada y devuelve un resumen, las afirmaciones y un read_id. No incluye el cuerpo de la página.",
        Schema,
        PermissionLevel.Read,
        "research",
        OutputTrust.Untrusted,
        WritesDurableState: false);

    public async ValueTask<ToolResult> ExecuteAsync(
        ToolArguments args, ToolExecutionContext context, CancellationToken ct)
    {
        if (!args.TryGetString("url", out var url) || string.IsNullOrWhiteSpace(url))
        {
            return ToolResult.Failed(
                args.TryGetString("url", out _)
                    ? "El parámetro 'url' no puede estar vacío."
                    : "Falta el parámetro obligatorio 'url'.");
        }

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var parsed))
        {
            return ToolResult.Failed("El parámetro 'url' no es una URL absoluta.");
        }

        if (!await budget.TryConsumeAsync(context.UserId, context.TurnId, ct).ConfigureAwait(false))
        {
            return ToolResult.Failed(QuotaExhaustedMessage);
        }

        var fetched = await fetcher.FetchAsync(parsed, ct).ConfigureAwait(false);
        if (!fetched.Succeeded || fetched.Body is null)
        {
            return ToolResult.Failed(fetched.Error ?? PageFetcher.OverCapMessage);
        }

        SourceSummary? summary;
        try
        {
            summary = await summarizer.SummarizeAsync(
                Encoding.UTF8.GetString(fetched.Body),
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

        if (summary is null)
        {
            return ToolResult.Failed(UncitableMessage);
        }

        var stored = await store.AddAsync(
            new NewResearchRead(
                context.UserId,
                context.ConversationId,
                context.TurnId,
                summary.Summary,
                summary.Claims,
                WithoutUserInfo(parsed)),
            ct).ConfigureAwait(false);

        return ToolResult.Ok(Present(stored), OutputTrust.Untrusted);
    }

    private static string Present(ResearchRead read)
        => JsonSerializer.Serialize(
            new
            {
                read_id = read.Id,
                summary = read.Summary,
                claims = read.Claims.Select(claim => new { text = claim.Text, fragment = claim.Fragment }).ToArray(),
            },
            OutputJson);

    private static string WithoutUserInfo(Uri uri)
    {
        if (string.IsNullOrEmpty(uri.UserInfo))
        {
            return uri.AbsoluteUri;
        }

        var builder = new UriBuilder(uri) { UserName = "", Password = "" };
        return builder.Uri.AbsoluteUri;
    }
}
