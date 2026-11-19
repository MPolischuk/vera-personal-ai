using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Assistant.Core.Research;
using Microsoft.Extensions.Options;

namespace Assistant.Extensions.Research;

/// <summary>
/// Posts bytes to <c>POST /v1/convert/file</c>. The form has no URL.
/// Chunks come from <c>json_content</c> (page, bbox, headings).
/// </summary>
internal sealed class DocumentConverter(HttpClient http, IOptions<ResearchOptions> options) : IDocumentConverter
{
    public const string HttpClientName = "docling";

    private static readonly Dictionary<string, string> FileNames = new(StringComparer.Ordinal)
    {
        ["application/pdf"] = "document.pdf",
        ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"] = "document.docx",
        ["application/vnd.openxmlformats-officedocument.presentationml.presentation"] = "document.pptx",
        ["text/html"] = "document.html",
    };

    public async ValueTask<IReadOnlyList<DocumentChunk>> ConvertAsync(
        ReadOnlyMemory<byte> content,
        string mediaType,
        CancellationToken cancellationToken)
    {
        if (options.Value.DoclingBaseUrl is null)
        {
            throw new InvalidOperationException("Docling is not configured.");
        }

        if (!FileNames.TryGetValue(mediaType, out var fileName))
        {
            throw new ArgumentException("The document type is not supported.", nameof(mediaType));
        }

        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("json"), "to_formats");
        form.Add(new StringContent("false"), "do_ocr");
        form.Add(new StringContent("placeholder"), "image_export_mode");
        form.Add(
            new StringContent(((int)options.Value.ConversionTimeout.TotalSeconds).ToString(CultureInfo.InvariantCulture)),
            "document_timeout");
        var file = new ByteArrayContent(content.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        form.Add(file, "files", fileName);

        using var response = await http.PostAsync(new Uri("v1/convert/file", UriKind.Relative), form, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return [];
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var payload = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return ReadChunks(payload.RootElement);
    }

    private static List<DocumentChunk> ReadChunks(JsonElement payload)
    {
        var chunks = new List<DocumentChunk>();
        if (payload.ValueKind is not JsonValueKind.Object)
        {
            return chunks;
        }

        if (payload.TryGetProperty("task_id", out _)
            && !payload.TryGetProperty("document", out _)
            && !payload.TryGetProperty("documents", out _))
        {
            return chunks;
        }

        if (payload.TryGetProperty("document", out var document))
        {
            ReadDocument(document, chunks);
        }

        if (payload.TryGetProperty("documents", out var documents) && documents.ValueKind is JsonValueKind.Array)
        {
            foreach (var item in documents.EnumerateArray())
            {
                ReadDocument(item, chunks);
            }
        }

        return chunks;
    }

    private static void ReadDocument(JsonElement document, List<DocumentChunk> chunks)
    {
        if (document.ValueKind is not JsonValueKind.Object)
        {
            return;
        }

        if (!document.TryGetProperty("json_content", out var content))
        {
            ReadItems(document, chunks);
            return;
        }

        if (content.ValueKind is JsonValueKind.String && content.GetString() is { } raw)
        {
            try
            {
                using var parsed = JsonDocument.Parse(raw);
                ReadItems(parsed.RootElement, chunks);
            }
            catch (JsonException)
            {
            }

            return;
        }

        ReadItems(content, chunks);
    }

    private static void ReadItems(JsonElement docling, List<DocumentChunk> chunks)
    {
        if (docling.ValueKind is not JsonValueKind.Object)
        {
            return;
        }

        var headings = new List<string>();
        if (docling.TryGetProperty("texts", out var texts) && texts.ValueKind is JsonValueKind.Array)
        {
            foreach (var item in texts.EnumerateArray())
            {
                AddItem(item, headings, chunks);
            }
        }

        if (docling.TryGetProperty("tables", out var tables) && tables.ValueKind is JsonValueKind.Array)
        {
            foreach (var item in tables.EnumerateArray())
            {
                AddItem(item, headings, chunks);
            }
        }
    }

    private static void AddItem(JsonElement item, List<string> headings, List<DocumentChunk> chunks)
    {
        if (item.ValueKind is not JsonValueKind.Object
            || !item.TryGetProperty("text", out var textProperty)
            || textProperty.ValueKind is not JsonValueKind.String
            || textProperty.GetString() is not { Length: > 0 } text
            || !TryReadProvenance(item, out var page, out var bbox))
        {
            return;
        }

        var label = item.TryGetProperty("label", out var labelProperty) && labelProperty.ValueKind is JsonValueKind.String
            ? labelProperty.GetString()
            : null;
        if (label is "title" or "section_header")
        {
            headings.Clear();
            headings.Add(text);
        }

        chunks.Add(new DocumentChunk(text, page, bbox, headings.ToArray()));
    }

    private static bool TryReadProvenance(JsonElement item, out int page, out ResearchBoundingBox bbox)
    {
        page = 0;
        bbox = new ResearchBoundingBox(0, 0, 0, 0);
        if (!item.TryGetProperty("prov", out var prov) || prov.ValueKind is not JsonValueKind.Array)
        {
            return false;
        }

        foreach (var entry in prov.EnumerateArray())
        {
            if (entry.ValueKind is not JsonValueKind.Object
                || !TryReadPage(entry, out page)
                || !entry.TryGetProperty("bbox", out var box)
                || box.ValueKind is not JsonValueKind.Object
                || !TryReadDouble(box, "l", out var left)
                || !TryReadDouble(box, "t", out var top)
                || !TryReadDouble(box, "r", out var right)
                || !TryReadDouble(box, "b", out var bottom))
            {
                continue;
            }

            bbox = new ResearchBoundingBox(left, top, right, bottom);
            return true;
        }

        return false;
    }

    private static bool TryReadPage(JsonElement entry, out int page)
    {
        if (entry.TryGetProperty("page_no", out var pageNo) && pageNo.TryGetInt32(out page))
        {
            return true;
        }

        if (entry.TryGetProperty("page", out var pageProperty) && pageProperty.TryGetInt32(out page))
        {
            return true;
        }

        page = 0;
        return false;
    }

    private static bool TryReadDouble(JsonElement element, string name, out double value)
    {
        if (element.TryGetProperty(name, out var property) && property.TryGetDouble(out value))
        {
            return true;
        }

        value = 0;
        return false;
    }
}
