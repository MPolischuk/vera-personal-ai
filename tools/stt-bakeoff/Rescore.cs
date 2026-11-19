using System.Globalization;
using System.Text.Json;

namespace SttBakeoff;

internal static class Rescore
{
    public static int Run(string resultsPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(resultsPath));
        var vendors = document.RootElement.GetProperty("vendors");
        Console.WriteLine("| Vendor | WER crudo | WER acentos | WER sin diacríticos | Entidades | ms final |");
        Console.WriteLine("|---|---:|---:|---:|---:|---:|");
        foreach (var vendor in vendors.EnumerateArray())
        {
            if (!vendor.TryGetProperty("withoutContext", out var phrases)
                || phrases.ValueKind != JsonValueKind.Array
                || phrases.GetArrayLength() == 0)
            {
                Console.WriteLine("| " + vendor.GetProperty("Vendor").GetString() + " |  |  |  |  |  |");
                continue;
            }

            var raw = new List<UtteranceScore>();
            var accent = new List<UtteranceScore>();
            var stripped = new List<UtteranceScore>();
            var milliseconds = 0;
            foreach (var phrase in phrases.EnumerateArray())
            {
                var reference = ReferenceFor(phrase.GetProperty("Id").GetString());
                var hypothesis = phrase.GetProperty("Hypothesis").GetString() ?? string.Empty;
                raw.Add(Scoring.Score(reference, hypothesis));
                accent.Add(Scoring.ScoreNormalized(reference, hypothesis, stripDiacritics: false));
                stripped.Add(Scoring.ScoreNormalized(reference, hypothesis, stripDiacritics: true));
                milliseconds += phrase.GetProperty("FinalMilliseconds").GetInt32();
            }

            var entity = Scoring.CorpusEntityWordErrorRate(accent);
            Console.WriteLine(
                "| " + vendor.GetProperty("Vendor").GetString()
                + " | " + Format(Scoring.CorpusWordErrorRate(raw))
                + " | " + Format(Scoring.CorpusWordErrorRate(accent))
                + " | " + Format(Scoring.CorpusWordErrorRate(stripped))
                + " | " + Format(entity)
                + " | " + (milliseconds / phrases.GetArrayLength()).ToString(CultureInfo.InvariantCulture)
                + " |");
        }

        return 0;
    }

    private static string ReferenceFor(string? id)
    {
        if (int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            && number >= 1
            && number <= GoldPhrases.Lines.Length)
        {
            return GoldPhrases.Lines[number - 1];
        }

        throw new InvalidOperationException("Unknown phrase id " + id + ".");
    }

    private static string Format(double? ratio)
    {
        return ratio is null ? string.Empty : ratio.Value.ToString("0.000", CultureInfo.InvariantCulture);
    }
}
