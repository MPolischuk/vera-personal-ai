namespace SttBakeoff;

internal sealed record BakeoffKeys(
    string? Deepgram,
    string? Mistral,
    string? OpenRouter,
    string? OpenAi,
    string? ElevenLabs)
{
    public static BakeoffKeys Load(string? envFile)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(envFile))
        {
            ReadFile(envFile, values);
        }
        else
        {
            var local = Path.Combine(Directory.GetCurrentDirectory(), ".env");
            if (File.Exists(local))
            {
                ReadFile(local, values);
            }
        }

        string? Pick(string name)
        {
            var fromProcess = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(fromProcess))
            {
                return fromProcess.Trim();
            }

            return values.TryGetValue(name, out var fromFile) && !string.IsNullOrWhiteSpace(fromFile)
                ? fromFile.Trim()
                : null;
        }

        var deepgram = Pick("STT_API_KEY") ?? Pick("DEEPGRAM_API_KEY");
        var openRouter = Pick("OPENROUTER_API_KEY") ?? Pick("LLM_API_KEY");
        var elevenLabs = Pick("ELEVENLABS_API_KEY") ?? Pick("TTS_API_KEY");
        return new BakeoffKeys(deepgram, Pick("MISTRAL_API_KEY"), openRouter, Pick("OPENAI_API_KEY"), elevenLabs);
    }

    public string PresenceLine()
    {
        return "keys deepgram=" + Flag(Deepgram)
            + " mistral=" + Flag(Mistral)
            + " openrouter=" + Flag(OpenRouter)
            + " openai=" + Flag(OpenAi)
            + " elevenlabs=" + Flag(ElevenLabs)
            + " nemotron=sin GPU";
    }

    private static string Flag(string? value)
    {
        return string.IsNullOrEmpty(value) ? "missing" : "set";
    }

    private static void ReadFile(string path, Dictionary<string, string> values)
    {
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith("export ", StringComparison.Ordinal))
            {
                line = line["export ".Length..].Trim();
            }

            var split = line.IndexOf('=');
            if (split <= 0)
            {
                continue;
            }

            var name = line[..split].Trim();
            var value = line[(split + 1)..].Trim();
            if (value.Length >= 2
                && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
            {
                value = value[1..^1];
            }

            values[name] = value;
        }
    }
}
