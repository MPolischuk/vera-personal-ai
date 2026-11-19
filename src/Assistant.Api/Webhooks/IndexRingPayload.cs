using System.Buffers;
using Microsoft.AspNetCore.Http.Features;

namespace Assistant.Api.Webhooks;

/// <summary>
/// Reads an Index 01 multipart body. The text field is <c>transcription</c>
/// (Index webhook contract, app commit 6361b3310). Audio is copied up to the
/// source cap and dropped before the method returns.
/// </summary>
internal static class IndexRingPayload
{
    public const string TranscriptionField = "transcription";

    public const int MaxTranscriptionChars = 4000;

    public static bool IsTest(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.Headers.TryGetValue("X-Index-Test", out var value)
            && string.Equals(value.ToString(), "true", StringComparison.Ordinal);
    }

    public static async Task<string?> ReadTranscriptionAsync(
        HttpRequest request, int maxBodyBytes, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.HasFormContentType)
        {
            return null;
        }

        // Keep every section in memory. The default form reader spills past 64 KiB to a temp file.
        request.HttpContext.Features.Set<IFormFeature>(new FormFeature(
            request,
            new FormOptions
            {
                BufferBody = true,
                MemoryBufferThreshold = int.MaxValue,
                MultipartBodyLengthLimit = int.MaxValue,
                ValueLengthLimit = int.MaxValue,
            }));

        IFormCollection form;
        try
        {
            form = await request.ReadFormAsync(ct);
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or InvalidDataException
            or BadHttpRequestException
            or IOException)
        {
            return null;
        }

        await DiscardFilesAsync(form.Files, maxBodyBytes, ct);

        var raw = form[TranscriptionField].ToString();
        if (raw.Length > MaxTranscriptionChars)
        {
            raw = raw[..MaxTranscriptionChars];
        }

        var transcription = raw.Trim();
        return transcription.Length == 0 ? null : transcription;
    }

    private static async Task DiscardFilesAsync(IFormFileCollection files, int cap, CancellationToken ct)
    {
        if (cap < 0)
        {
            cap = 0;
        }

        foreach (var file in files)
        {
            var rented = ArrayPool<byte>.Shared.Rent(16 * 1024);
            try
            {
                await using var buffer = new MemoryStream();
                await using var source = file.OpenReadStream();
                if (source is FileStream)
                {
                    throw new InvalidOperationException("Index ring audio must not be buffered to disk.");
                }

                var kept = 0;
                while (kept < cap)
                {
                    var room = Math.Min(rented.Length, cap - kept);
                    var read = await source.ReadAsync(rented.AsMemory(0, room), ct);
                    if (read == 0)
                    {
                        break;
                    }

                    buffer.Write(rented, 0, read);
                    kept += read;
                }

                buffer.SetLength(0);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented, clearArray: true);
            }
        }
    }
}
