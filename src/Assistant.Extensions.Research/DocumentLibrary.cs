using Assistant.Core.Research;

namespace Assistant.Extensions.Research;

/// <summary>
/// Stores an upload as <c>{root}/{userId}/{id}</c> plus a sibling media-type file.
/// </summary>
public sealed class DocumentLibrary(string root, int maxBytes) : IDocumentLibrary
{
    public const int DefaultMaxBytes = 15 * 1024 * 1024;

    private static readonly Dictionary<string, string> MediaByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        [".html"] = "text/html",
        [".htm"] = "text/html",
    };

    public static bool IsAllowed(string? mediaType)
        => mediaType is not null && MediaByExtension.ContainsValue(mediaType);

    public static string? ResolveMediaType(string? fileName, string? contentType)
    {
        var extension = Path.GetExtension(fileName ?? "");
        if (MediaByExtension.TryGetValue(extension, out var fromName))
        {
            return fromName;
        }

        if (string.IsNullOrWhiteSpace(contentType))
        {
            return null;
        }

        var bare = contentType.Split(';', 2)[0].Trim();
        return IsAllowed(bare) ? bare : null;
    }

    public async ValueTask<Guid> SaveAsync(
        Guid userId,
        Stream content,
        string mediaType,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (!IsAllowed(mediaType))
        {
            throw new ArgumentException("The document type is not supported.", nameof(mediaType));
        }

        var id = Guid.CreateVersion7();
        var path = UserFile(userId, id);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".partial";
        var total = 0L;
        try
        {
            await using (var target = new FileStream(
                temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                while (true)
                {
                    var read = await content.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }

                    total += read;
                    if (total > maxBytes)
                    {
                        throw new DocumentTooLargeException();
                    }

                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }
            }

            if (total == 0)
            {
                throw new ArgumentException("The document is empty.", nameof(content));
            }

            File.Move(temporary, path);
            await File.WriteAllTextAsync(path + ".media", mediaType, cancellationToken).ConfigureAwait(false);
            return id;
        }
        catch
        {
            TryDelete(temporary);
            TryDelete(path);
            TryDelete(path + ".media");
            throw;
        }
    }

    public async ValueTask<StoredDocument?> OpenAsync(
        Guid userId,
        Guid documentId,
        CancellationToken cancellationToken)
    {
        var path = UserFile(userId, documentId);
        if (!File.Exists(path) || !File.Exists(path + ".media"))
        {
            return null;
        }

        var mediaType = (await File.ReadAllTextAsync(path + ".media", cancellationToken).ConfigureAwait(false)).Trim();
        if (!IsAllowed(mediaType))
        {
            return null;
        }

        var content = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        if (content.Length == 0 || content.Length > maxBytes)
        {
            return null;
        }

        return new StoredDocument(documentId, mediaType, content);
    }

    private string UserFile(Guid userId, Guid documentId)
    {
        var libraryRoot = Path.GetFullPath(root);
        var userRoot = Path.GetFullPath(Path.Combine(libraryRoot, userId.ToString("N")))
            + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(userRoot, documentId.ToString("N")));
        if (!path.StartsWith(userRoot, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The document path escaped the library.");
        }

        return path;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

public sealed class DocumentTooLargeException : Exception
{
    public DocumentTooLargeException()
        : base("The document is larger than the configured limit.")
    {
    }
}
