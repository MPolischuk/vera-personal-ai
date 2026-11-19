using System.Globalization;
using System.IO.Compression;
using System.Text;
using Assistant.Core.Notes;
using Assistant.Core.Research;

namespace Assistant.Extensions.Notes;

public static class NoteArchive
{
    public static byte[] Build(IReadOnlyList<Note> notes)
    {
        ArgumentNullException.ThrowIfNull(notes);

        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var note in notes)
            {
                var entry = zip.CreateEntry(FileName(note, used), CompressionLevel.Fastest);
                using var writer = new StreamWriter(
                    entry.Open(),
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                writer.Write(Render(note));
            }
        }

        return stream.ToArray();
    }

    private static string FileName(Note note, HashSet<string> used)
    {
        var stem = Sanitize(note.Title);
        var suffix = note.Id.ToString("N")[..8];
        var name = $"{stem}-{suffix}.md";
        var next = name;
        var counter = 2;
        while (!used.Add(next))
        {
            next = $"{stem}-{suffix}-{counter}.md";
            counter++;
        }

        return next;
    }

    private static string Sanitize(string title)
    {
        var builder = new StringBuilder();
        foreach (var ch in title.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                builder.Append(ch);
            }
            else if (ch is >= 'A' and <= 'Z')
            {
                builder.Append(char.ToLowerInvariant(ch));
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }

            if (builder.Length >= 40)
            {
                break;
            }
        }

        var stem = builder.ToString().Trim('-');
        return stem.Length == 0 ? "nota" : stem;
    }

    private static string Render(Note note)
    {
        var builder = new StringBuilder();
        builder.AppendLine("---");
        builder.Append("verified_at: ");
        builder.AppendLine(note.VerifiedAt is { } verified ? YamlQuote(verified.ToString("O")) : "null");
        builder.AppendLine("sources:");
        if (note.Sources.Count == 0)
        {
            builder.AppendLine("  []");
        }
        else
        {
            foreach (var source in note.Sources)
            {
                builder.Append("  - claim: ");
                builder.AppendLine(YamlQuote(source.ClaimText));
                builder.Append("    fragment: ");
                builder.AppendLine(YamlQuote(source.Fragment));
                builder.Append("    retrieved_at: ");
                builder.AppendLine(YamlQuote(source.RetrievedAt.ToString("O")));
                if (source.DocumentId is { } documentId)
                {
                    builder.Append("    document_id: ");
                    builder.AppendLine(YamlQuote(documentId.ToString("D")));
                    builder.Append("    page: ");
                    builder.AppendLine(source.Page?.ToString(CultureInfo.InvariantCulture) ?? "null");
                    builder.Append("    bbox: ");
                    builder.AppendLine(FormatBbox(source.Bbox));
                }
                else
                {
                    builder.Append("    url: ");
                    builder.AppendLine(YamlQuote(source.Url ?? ""));
                }
            }
        }

        builder.AppendLine("---");
        builder.AppendLine();
        builder.AppendLine(note.Body);
        foreach (var neighbor in note.Neighbors)
        {
            builder.Append("[[");
            builder.Append(neighbor.Title.Replace("]]", "", StringComparison.Ordinal));
            builder.AppendLine("]]");
        }

        return builder.ToString();
    }

    private static string FormatBbox(ResearchBoundingBox? bbox)
    {
        if (bbox is null)
        {
            return "null";
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"[{bbox.Left}, {bbox.Top}, {bbox.Right}, {bbox.Bottom}]");
    }

    private static string YamlQuote(string value)
    {
        var escaped = value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
        return $"\"{escaped}\"";
    }
}
