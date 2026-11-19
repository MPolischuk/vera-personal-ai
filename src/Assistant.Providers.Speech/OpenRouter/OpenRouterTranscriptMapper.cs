using System.Text.Json;

namespace Assistant.Providers.Speech.OpenRouter;

/// <summary>Maps an OpenRouter audio transcription JSON body to text. Pure; no network.</summary>
internal static class OpenRouterTranscriptMapper
{
    public static bool TryMap(string json, out string text)
    {
        text = string.Empty;
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("text", out var textProperty))
        {
            return false;
        }

        text = textProperty.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(text);
    }
}
