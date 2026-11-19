using System.Text;
using System.Text.Json;
using Assistant.Core.Models;
using Assistant.Core.Research;
using Microsoft.Extensions.AI;

namespace Assistant.Extensions.Research;

/// <summary>
/// One generate with no tools. The trimmed page is the user message.
/// The reply is JSON; anything else is discarded and nothing is stored.
/// </summary>
internal sealed class SourceSummarizer(IModelRouter router)
{
    internal const int MaxChunkCharacters = 24_000;

    internal const string Instructions =
        "Leés solo el texto de la página que te paso. No llamás tools y no le hablás al usuario. "
        + "Respondé únicamente con un JSON de esta forma: "
        + "{\"summary\":\"…\",\"claims\":[{\"text\":\"…\",\"fragment\":\"…\"}]}. "
        + "summary es el resumen. claims son las afirmaciones. "
        + "text es la afirmación y fragment es el fragmento de la página que la sostiene. "
        + "No agregues nada fuera del JSON.";

    public Task<SourceSummary?> SummarizeChunksAsync(
        IReadOnlyList<DocumentChunk> chunks,
        ModelCallContext call,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        return SummarizeAsync(FormatChunks(chunks), call, cancellationToken);
    }

    internal static string FormatChunks(IReadOnlyList<DocumentChunk> chunks)
    {
        var builder = new StringBuilder();
        foreach (var chunk in chunks)
        {
            var block = new StringBuilder();
            if (chunk.Headings.Count > 0)
            {
                block.Append("## ");
                block.AppendJoin(" / ", chunk.Headings);
                block.Append('\n');
            }

            block.Append(chunk.Text);
            block.Append('\n');
            var blockText = block.ToString();
            if (builder.Length >= MaxChunkCharacters)
            {
                break;
            }

            if (builder.Length + blockText.Length > MaxChunkCharacters)
            {
                builder.Append(blockText.AsSpan(0, MaxChunkCharacters - builder.Length));
                break;
            }

            builder.Append(blockText);
        }

        return builder.ToString();
    }

    public async Task<SourceSummary?> SummarizeAsync(
        string pageText,
        ModelCallContext call,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pageText);
        ArgumentNullException.ThrowIfNull(call);

        var client = router.GetClientFor(TaskKind.Summarization, call);
        var response = await client.GetResponseAsync(
            [
                new ChatMessage(ChatRole.System, Instructions),
                new ChatMessage(ChatRole.User, pageText),
            ],
            new ChatOptions
            {
                Tools = null,
                ToolMode = ChatToolMode.None,
            },
            cancellationToken).ConfigureAwait(false);

        return TryParse(response.Text);
    }

    private static SourceSummary? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind is not JsonValueKind.Object
                || !TryReadString(document.RootElement, "summary", out var summary)
                || !document.RootElement.TryGetProperty("claims", out var claims)
                || claims.ValueKind is not JsonValueKind.Array)
            {
                return null;
            }

            var parsed = new List<ResearchClaim>();
            foreach (var claim in claims.EnumerateArray())
            {
                if (claim.ValueKind is not JsonValueKind.Object
                    || !TryReadString(claim, "text", out var claimText)
                    || !TryReadString(claim, "fragment", out var fragment))
                {
                    return null;
                }

                parsed.Add(new ResearchClaim(claimText, fragment));
            }

            return new SourceSummary(summary, parsed);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryReadString(JsonElement element, string name, out string value)
    {
        if (element.TryGetProperty(name, out var property)
            && property.ValueKind is JsonValueKind.String
            && property.GetString() is { } text)
        {
            value = text;
            return true;
        }

        value = "";
        return false;
    }
}

internal sealed record SourceSummary(string Summary, IReadOnlyList<ResearchClaim> Claims);
