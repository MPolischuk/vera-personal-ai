namespace SttBakeoff;

internal readonly record struct UtteranceScore(
    int Edits,
    int ReferenceWords,
    int EntityEdits,
    int EntityReferenceWords)
{
    public double WordErrorRate => ReferenceWords == 0 ? 0 : (double)Edits / ReferenceWords;

    public double? EntityWordErrorRate =>
        EntityReferenceWords == 0 ? null : (double)EntityEdits / EntityReferenceWords;
}

/// <summary>
/// Word error rate. Raw tokens are lowercased with punctuation removed.
/// Normalized tokens also rewrite spoken numbers to Spanish words, with or without diacritics.
/// An empty hypothesis scores 1 when the reference has words.
/// Entity rate counts alignment errors that touch Vera, Alicia, Coolify, or Buenos Aires.
/// The ranking that decides uses the normalized tokens that keep accents.
/// </summary>
internal static class Scoring
{
    private static readonly HashSet<string> EntityTokens = new(StringComparer.Ordinal)
    {
        "vera",
        "alicia",
        "coolify",
        "buenos",
        "aires",
    };

    public static string[] Tokenize(string text)
    {
        var tokens = new List<string>();
        var current = new System.Text.StringBuilder();
        foreach (var ch in text.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                current.Append(ch);
                continue;
            }

            if (current.Length > 0)
            {
                tokens.Add(current.ToString());
                current.Clear();
            }
        }

        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
        }

        return [.. tokens];
    }

    public static string[] TokenizeNormalized(string text, bool stripDiacritics)
    {
        var tokens = Tokenize(SpanishNumbers.Expand(text.ToLowerInvariant()));
        if (!stripDiacritics)
        {
            return tokens;
        }

        var stripped = new string[tokens.Length];
        for (var i = 0; i < tokens.Length; i++)
        {
            stripped[i] = SpanishNumbers.StripDiacritics(tokens[i]);
        }

        return stripped;
    }

    public static UtteranceScore Score(string reference, string hypothesis)
        => ScoreTokens(Tokenize(reference), Tokenize(hypothesis));

    public static UtteranceScore ScoreNormalized(string reference, string hypothesis, bool stripDiacritics)
        => ScoreTokens(
            TokenizeNormalized(reference, stripDiacritics),
            TokenizeNormalized(hypothesis, stripDiacritics));

    private static UtteranceScore ScoreTokens(string[] referenceTokens, string[] hypothesisTokens)
    {
        var entityReferenceWords = 0;
        foreach (var token in referenceTokens)
        {
            if (IsEntity(token))
            {
                entityReferenceWords++;
            }
        }

        if (hypothesisTokens.Length == 0)
        {
            return new UtteranceScore(
                referenceTokens.Length,
                referenceTokens.Length,
                entityReferenceWords,
                entityReferenceWords);
        }

        return Align(referenceTokens, hypothesisTokens, entityReferenceWords);
    }

    public static double CorpusWordErrorRate(IReadOnlyList<UtteranceScore> scores)
    {
        var edits = 0;
        var words = 0;
        foreach (var score in scores)
        {
            edits += score.Edits;
            words += score.ReferenceWords;
        }

        return words == 0 ? 0 : (double)edits / words;
    }

    public static double? CorpusEntityWordErrorRate(IReadOnlyList<UtteranceScore> scores)
    {
        var edits = 0;
        var words = 0;
        foreach (var score in scores)
        {
            edits += score.EntityEdits;
            words += score.EntityReferenceWords;
        }

        return words == 0 ? null : (double)edits / words;
    }

    public static double MeanPhraseWordErrorRate(IReadOnlyList<UtteranceScore> scores)
    {
        if (scores.Count == 0)
        {
            return 0;
        }

        var sum = 0.0;
        foreach (var score in scores)
        {
            sum += score.WordErrorRate;
        }

        return sum / scores.Count;
    }

    public static int SelfTest()
    {
        var failures = 0;
        failures += Expect(
            "identical ignoring case and punctuation",
            Score("¿Me buscás el mail de Alicia?", "me buscás el mail de alicia"),
            edits: 0,
            entityEdits: 0);
        failures += Expect(
            "empty hypothesis is WER 1",
            Score("Acordame llamar a Vera a las cinco.", ""),
            edits: 7,
            entityEdits: 1);
        failures += ExpectRatio("empty hypothesis ratio", Score("Vera", "").WordErrorRate, 1);
        failures += Expect(
            "one entity substitution",
            Score("Che, fijate si mañana llueve en Buenos Aires.", "che fijate si mañana llueve en buenos aries"),
            edits: 1,
            entityEdits: 1);
        failures += ExpectRatio(
            "entity substitution ratio",
            Score("en Buenos Aires", "en buenos aries").EntityWordErrorRate ?? -1,
            0.5);
        failures += Expect(
            "non-entity insertion is not an entity error",
            Score("Vera", "vera che"),
            edits: 1,
            entityEdits: 0);
        var redacted = SecretRedaction.Redact("token sk-live-secret failed", "sk-live-secret");
        if (redacted.Contains("sk-live-secret", StringComparison.Ordinal))
        {
            Console.Error.WriteLine("redaction failed");
            failures++;
        }

        failures += ExpectSameTokens(
            "digits and words share accent-normalized tokens",
            "Acordame llamar a Vera a las 5.",
            "Acordame llamar a Vera a las cinco.");
        failures += ExpectSameTokens(
            "a date in digits and words",
            "el 15 de marzo",
            "el quince de marzo");
        failures += ExpectSameTokens(
            "an amount in digits and words",
            "son $1.500",
            "son mil quinientos");
        failures += ExpectSameTokens(
            "a half hour in digits and words",
            "a las 5:30",
            "a las cinco y media");
        var rawDigits = Tokenize("a las 5");
        var rawWords = Tokenize("a las cinco");
        if (rawDigits.SequenceEqual(rawWords))
        {
            Console.Error.WriteLine("raw tokenize collapsed digits and words");
            failures++;
        }

        var withAccents = TokenizeNormalized("mañana está", stripDiacritics: false);
        var withoutAccents = TokenizeNormalized("mañana está", stripDiacritics: true);
        var alreadyPlain = TokenizeNormalized("manana esta", stripDiacritics: false);
        if (withAccents.SequenceEqual(withoutAccents) || !withoutAccents.SequenceEqual(alreadyPlain))
        {
            Console.Error.WriteLine(
                "diacritics: [" + string.Join(' ', withAccents) + "] vs [" + string.Join(' ', withoutAccents) + "]");
            failures++;
        }

        foreach (var line in GoldPhrases.Lines)
        {
            if (line.Any(char.IsDigit))
            {
                Console.Error.WriteLine("gold reference still has digits: " + line);
                failures++;
            }
        }

        var energyPcm = new byte[(5 + 40) * 640];
        for (var i = 0; i < 5 * 320; i++)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(energyPcm.AsSpan(i * 2, 2), 1000);
        }

        var energyAt = EnergyEndpoint.EndpointSeconds(energyPcm);
        if (Math.Abs(energyAt - 0.9) > 0.001)
        {
            Console.Error.WriteLine(
                "energy endpoint " + energyAt.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) + " (want 0.900)");
            failures++;
        }

        if (failures == 0)
        {
            Console.WriteLine("self-test passed");
        }

        return failures == 0 ? 0 : 1;
    }

    private static UtteranceScore Align(
        string[] reference,
        string[] hypothesis,
        int entityReferenceWords)
    {
        var rows = reference.Length + 1;
        var columns = hypothesis.Length + 1;
        var distance = new int[rows, columns];
        for (var i = 0; i < rows; i++)
        {
            distance[i, 0] = i;
        }

        for (var j = 0; j < columns; j++)
        {
            distance[0, j] = j;
        }

        for (var i = 1; i < rows; i++)
        {
            for (var j = 1; j < columns; j++)
            {
                var substitutionCost = string.Equals(reference[i - 1], hypothesis[j - 1], StringComparison.Ordinal)
                    ? 0
                    : 1;
                distance[i, j] = Math.Min(
                    Math.Min(distance[i - 1, j] + 1, distance[i, j - 1] + 1),
                    distance[i - 1, j - 1] + substitutionCost);
            }
        }

        var entityEdits = 0;
        var iCursor = reference.Length;
        var jCursor = hypothesis.Length;
        while (iCursor > 0 || jCursor > 0)
        {
            if (iCursor > 0 && jCursor > 0)
            {
                var substitutionCost = string.Equals(
                    reference[iCursor - 1],
                    hypothesis[jCursor - 1],
                    StringComparison.Ordinal)
                    ? 0
                    : 1;
                if (distance[iCursor, jCursor] == distance[iCursor - 1, jCursor - 1] + substitutionCost)
                {
                    if (substitutionCost == 1
                        && (IsEntity(reference[iCursor - 1]) || IsEntity(hypothesis[jCursor - 1])))
                    {
                        entityEdits++;
                    }

                    iCursor--;
                    jCursor--;
                    continue;
                }
            }

            if (iCursor > 0 && distance[iCursor, jCursor] == distance[iCursor - 1, jCursor] + 1)
            {
                if (IsEntity(reference[iCursor - 1]))
                {
                    entityEdits++;
                }

                iCursor--;
                continue;
            }

            if (IsEntity(hypothesis[jCursor - 1]))
            {
                entityEdits++;
            }

            jCursor--;
        }

        return new UtteranceScore(distance[reference.Length, hypothesis.Length], reference.Length, entityEdits, entityReferenceWords);
    }

    private static bool IsEntity(string token)
    {
        return EntityTokens.Contains(token);
    }

    private static int ExpectSameTokens(string name, string digits, string words)
    {
        var fromDigits = TokenizeNormalized(digits, stripDiacritics: false);
        var fromWords = TokenizeNormalized(words, stripDiacritics: false);
        if (fromDigits.SequenceEqual(fromWords))
        {
            return 0;
        }

        Console.Error.WriteLine(
            name + ": [" + string.Join(' ', fromDigits) + "] vs [" + string.Join(' ', fromWords) + "]");
        return 1;
    }

    private static int Expect(string name, UtteranceScore score, int edits, int entityEdits)
    {
        if (score.Edits == edits && score.EntityEdits == entityEdits)
        {
            return 0;
        }

        Console.Error.WriteLine($"{name}: edits {score.Edits} (want {edits}), entity edits {score.EntityEdits} (want {entityEdits})");
        return 1;
    }

    private static int ExpectRatio(string name, double actual, double expected)
    {
        if (Math.Abs(actual - expected) < 0.0001)
        {
            return 0;
        }

        Console.Error.WriteLine($"{name}: {actual} (want {expected})");
        return 1;
    }
}
