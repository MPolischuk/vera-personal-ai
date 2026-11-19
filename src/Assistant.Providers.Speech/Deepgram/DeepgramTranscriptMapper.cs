using System.Text.Json;

namespace Assistant.Providers.Speech.Deepgram;

/// <summary>Maps Deepgram listen WebSocket JSON to port events. Pure; no network.</summary>
internal static class DeepgramTranscriptMapper
{
    public static bool TryMap(string json, out string text, out bool isFinal, out double? confidence)
    {
        text = string.Empty;
        isFinal = false;
        confidence = null;

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (!root.TryGetProperty("type", out var typeProperty)
            || !string.Equals(typeProperty.GetString(), "Results", StringComparison.Ordinal))
        {
            return false;
        }

        if (!root.TryGetProperty("channel", out var channel)
            || !channel.TryGetProperty("alternatives", out var alternatives)
            || alternatives.GetArrayLength() == 0)
        {
            return false;
        }

        var first = alternatives[0];
        text = first.TryGetProperty("transcript", out var transcript)
            ? transcript.GetString() ?? string.Empty
            : string.Empty;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        isFinal = root.TryGetProperty("is_final", out var isFinalProperty)
            && isFinalProperty.ValueKind is JsonValueKind.True;

        if (first.TryGetProperty("confidence", out var confidenceProperty)
            && confidenceProperty.ValueKind is JsonValueKind.Number)
        {
            confidence = confidenceProperty.GetDouble();
        }

        return true;
    }

    /// <summary>Pre-recorded <c>/v1/listen</c> JSON (no <c>type: Results</c> envelope).</summary>
    public static bool TryMapPrerecorded(string json, out string text)
    {
        text = string.Empty;
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("results", out var results)
            || !results.TryGetProperty("channels", out var channels)
            || channels.GetArrayLength() == 0)
        {
            return false;
        }

        var channel = channels[0];
        if (!channel.TryGetProperty("alternatives", out var alternatives)
            || alternatives.GetArrayLength() == 0)
        {
            return false;
        }

        var first = alternatives[0];
        text = first.TryGetProperty("transcript", out var transcript)
            ? transcript.GetString() ?? string.Empty
            : string.Empty;
        return !string.IsNullOrWhiteSpace(text);
    }

    /// <summary>
    /// Empty <c>from_finalize</c> result: Deepgram flushed remaining audio with no text.
    /// </summary>
    public static bool IsFinalizeAck(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        return root.TryGetProperty("type", out var typeProperty)
            && string.Equals(typeProperty.GetString(), "Results", StringComparison.Ordinal)
            && root.TryGetProperty("from_finalize", out var fromFinalize)
            && fromFinalize.ValueKind is JsonValueKind.True;
    }

    public static bool TryReadError(string json, out string message)
    {
        message = string.Empty;
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("type", out var typeProperty)
            || !string.Equals(typeProperty.GetString(), "Error", StringComparison.Ordinal))
        {
            return false;
        }

        message = root.TryGetProperty("message", out var messageProperty)
            ? messageProperty.GetString() ?? "unknown error"
            : "unknown error";
        return true;
    }
}
