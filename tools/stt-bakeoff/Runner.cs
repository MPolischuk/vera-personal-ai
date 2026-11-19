using System.Globalization;
using System.Text;
using System.Text.Json;

namespace SttBakeoff;

internal sealed class PhraseMeasurement
{
    public required string Id { get; init; }

    public required string Hypothesis { get; init; }

    public required int Edits { get; init; }

    public required int ReferenceWords { get; init; }

    public required int EntityEdits { get; init; }

    public required int EntityReferenceWords { get; init; }

    public required int RawEdits { get; init; }

    public required int RawReferenceWords { get; init; }

    public required int StrippedEdits { get; init; }

    public required int StrippedReferenceWords { get; init; }

    public required int FinalMilliseconds { get; init; }

    public int? FirstPartialMilliseconds { get; init; }

    public string? Note { get; init; }

    public UtteranceScore Score => new(Edits, ReferenceWords, EntityEdits, EntityReferenceWords);
}

internal sealed class VendorMeasurement
{
    public required string Vendor { get; init; }

    public required string Model { get; init; }

    public required string Mode { get; init; }

    public required string Status { get; init; }

    public required string Notes { get; init; }

    public bool ContextAccepted { get; init; }

    public IReadOnlyList<PhraseMeasurement> WithoutContext { get; init; } = [];

    public IReadOnlyList<PhraseMeasurement> WithContext { get; init; } = [];
}

internal static class Runner
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static readonly string[] NewCandidates =
    [
        "Voxtral Realtime",
        "Voxtral Mini Transcribe V2",
        "Scribe v2",
    ];

    public static async Task<int> RunAsync(string sampleDir, string? envFile, string? only, CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(sampleDir);
        var clips = WavSample.Load(root);
        var keys = BakeoffKeys.Load(envFile);
        Console.WriteLine(keys.PresenceLine());
        foreach (var clip in clips)
        {
            var words = Scoring.Tokenize(clip.Phrase).Length;
            var pace = words == 0 ? 0 : clip.DurationSeconds / words;
            Console.WriteLine($"{clip.Id}  {clip.DurationSeconds.ToString("0.00", CultureInfo.InvariantCulture)}s  {pace.ToString("0.00", CultureInfo.InvariantCulture)}s/word");
        }

        var rows = new List<VendorMeasurement>();
        if (Selected(only, "deepgram"))
        {
            rows.Add(await MeasureAsync(
                "Deepgram",
                "nova-3",
                "streaming",
                keys.Deepgram,
                clips,
                Vendors.DeepgramAsync,
                "es-419; texto final del listen prerecorded; parcial del WebSocket",
                cancellationToken).ConfigureAwait(false));
        }

        if (Selected(only, "voxtral-realtime"))
        {
            rows.Add(await MeasureAsync(
                "Voxtral Realtime",
                "voxtral-mini-transcribe-realtime-2602",
                "streaming",
                keys.Mistral,
                clips,
                Vendors.VoxtralRealtimeAsync,
                "delay 480 ms; sin forzar idioma; context_bias en la pasada con contexto",
                cancellationToken).ConfigureAwait(false));
        }

        if (Selected(only, "voxtral-batch"))
        {
            rows.Add(await MeasureAsync(
                "Voxtral Mini Transcribe V2",
                "voxtral-mini-2602",
                "batch",
                keys.Mistral,
                clips,
                Vendors.VoxtralBatchAsync,
                "context_bias en la pasada con contexto",
                cancellationToken).ConfigureAwait(false));
        }

        if (Selected(only, "scribe"))
        {
            rows.Add(await MeasureAsync(
                "Scribe v2",
                "elevenlabs/scribe-v2",
                "batch",
                keys.OpenRouter,
                clips,
                Vendors.ScribeAsync,
                "OpenRouter; keyterms en la pasada con contexto",
                cancellationToken).ConfigureAwait(false));
        }

        if (Selected(only, "scribe-realtime"))
        {
            rows.Add(await MeasureAsync(
                "Scribe v2 Realtime",
                "scribe_v2_realtime",
                "streaming",
                keys.ElevenLabs,
                clips,
                Vendors.ScribeRealtimeAsync,
                "ElevenLabs directo; commit manual; enable_logging=false; keyterms en la pasada con contexto",
                cancellationToken).ConfigureAwait(false));
        }

        if (Selected(only, "flux"))
        {
            rows.Add(await MeasureAsync(
                "Deepgram Flux",
                "flux-general-multi",
                "streaming",
                keys.Deepgram,
                clips,
                Vendors.FluxAsync,
                "language_hint=es; misma clave; el fin de turno del servidor no reemplaza la energía del cliente",
                cancellationToken).ConfigureAwait(false));
        }

        if (Selected(only, "openai"))
        {
            rows.Add(await MeasureAsync(
                "gpt-4o-transcribe",
                "gpt-4o-transcribe",
                "batch",
                keys.OpenAi,
                clips,
                Vendors.OpenAiAsync,
                "prompt con los cuatro términos en la pasada con contexto",
                cancellationToken).ConfigureAwait(false));
        }

        if (Selected(only, "nemotron"))
        {
            rows.Add(DataRow("Nemotron 3.5 ASR 0.6B", "nemotron-3.5-asr-streaming-0.6b", "sin GPU"));
            rows.Add(DataRow("Nemotron 3.5 Transcribe", "nemotron-3.5-transcribe", "sin GPU"));
        }

        var decision = Decide(rows);
        var doneWhen = DoneWhenMet(rows);
        var table = RenderTable(rows, decision, doneWhen);
        Console.WriteLine();
        Console.WriteLine(table);

        var suffix = string.IsNullOrWhiteSpace(only) ? string.Empty : "." + only.Trim();
        var jsonPath = Path.Combine(root, "bakeoff-results" + suffix + ".json");
        var markdownPath = Path.Combine(root, "bakeoff-table" + suffix + ".md");
        await File.WriteAllTextAsync(markdownPath, table, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(jsonPath, RenderJson(root, clips, rows, decision, doneWhen), cancellationToken)
            .ConfigureAwait(false);
        Console.WriteLine();
        Console.WriteLine("Wrote " + markdownPath);
        Console.WriteLine("Wrote " + jsonPath);
        return 0;
    }

    private static bool Selected(string? only, string name)
    {
        return string.IsNullOrWhiteSpace(only)
            || string.Equals(only.Trim(), name, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<VendorMeasurement> MeasureAsync(
        string vendor,
        string model,
        string mode,
        string? apiKey,
        IReadOnlyList<WavClip> clips,
        Func<WavClip, string, bool, CancellationToken, Task<PassResult>> call,
        string note,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            return new VendorMeasurement
            {
                Vendor = vendor,
                Model = model,
                Mode = mode,
                Status = "sin clave",
                Notes = "sin clave",
            };
        }

        var without = new List<PhraseMeasurement>();
        var with = new List<PhraseMeasurement>();
        var notes = new List<string> { note };
        var contextAccepted = true;
        var serverEarly = 0;
        var serverCompared = 0;
        foreach (var clip in clips)
        {
            try
            {
                var plain = await call(clip, apiKey, false, cancellationToken).ConfigureAwait(false);
                without.Add(ToPhrase(clip, plain));
                if (plain.ServerEndpointBeforeEnergy is bool early)
                {
                    serverCompared++;
                    if (early)
                    {
                        serverEarly++;
                    }
                }

                if (!string.IsNullOrWhiteSpace(plain.Note))
                {
                    notes.Add(clip.Id + " sin contexto: " + plain.Note);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return Failed(vendor, model, mode, notes, ex, apiKey);
            }

            if (!contextAccepted)
            {
                continue;
            }

            try
            {
                var biased = await call(clip, apiKey, true, cancellationToken).ConfigureAwait(false);
                with.Add(ToPhrase(clip, biased));
                if (!string.IsNullOrWhiteSpace(biased.Note))
                {
                    notes.Add(clip.Id + " con contexto: " + biased.Note);
                }
            }
            catch (ContextRejectedException)
            {
                contextAccepted = false;
                with.Clear();
                notes.Add("el API no acepta términos");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return Failed(vendor, model, mode, notes, ex, apiKey);
            }
        }

        if (serverCompared > 0)
        {
            notes.Add(
                "fin de turno del servidor antes que la energía de 800 ms: "
                + serverEarly.ToString(CultureInfo.InvariantCulture)
                + "/"
                + serverCompared.ToString(CultureInfo.InvariantCulture));
        }

        return new VendorMeasurement
        {
            Vendor = vendor,
            Model = model,
            Mode = mode,
            Status = "ok",
            Notes = string.Join(" ", notes.Distinct(StringComparer.Ordinal)),
            ContextAccepted = contextAccepted,
            WithoutContext = without,
            WithContext = with,
        };
    }

    private static VendorMeasurement Failed(
        string vendor,
        string model,
        string mode,
        List<string> notes,
        Exception exception,
        string apiKey)
    {
        notes.Add(SecretRedaction.Snippet(exception.Message, 180, apiKey));
        return new VendorMeasurement
        {
            Vendor = vendor,
            Model = model,
            Mode = mode,
            Status = "error",
            Notes = string.Join(" ", notes.Distinct(StringComparer.Ordinal)),
        };
    }

    private static VendorMeasurement DataRow(string vendor, string model, string status)
    {
        return new VendorMeasurement
        {
            Vendor = vendor,
            Model = model,
            Mode = "data",
            Status = status,
            Notes = status,
        };
    }

    private static PhraseMeasurement ToPhrase(WavClip clip, PassResult pass)
    {
        var raw = Scoring.Score(clip.Phrase, pass.Text);
        var accent = Scoring.ScoreNormalized(clip.Phrase, pass.Text, stripDiacritics: false);
        var stripped = Scoring.ScoreNormalized(clip.Phrase, pass.Text, stripDiacritics: true);
        return new PhraseMeasurement
        {
            Id = clip.Id,
            Hypothesis = pass.Text,
            Edits = accent.Edits,
            ReferenceWords = accent.ReferenceWords,
            EntityEdits = accent.EntityEdits,
            EntityReferenceWords = accent.EntityReferenceWords,
            RawEdits = raw.Edits,
            RawReferenceWords = raw.ReferenceWords,
            StrippedEdits = stripped.Edits,
            StrippedReferenceWords = stripped.ReferenceWords,
            FinalMilliseconds = pass.FinalMilliseconds,
            FirstPartialMilliseconds = pass.FirstPartialMilliseconds,
            Note = pass.Note,
        };
    }

    private static string Decide(IReadOnlyList<VendorMeasurement> rows)
    {
        var deepgram = rows.FirstOrDefault(row => row.Vendor == "Deepgram" && row.Status == "ok");
        VendorMeasurement? best = null;
        foreach (var row in rows)
        {
            if (!IsProductionCandidate(row))
            {
                continue;
            }

            if (best is null || IsBetter(row, best))
            {
                best = row;
            }
        }

        if (best is null || deepgram is null)
        {
            return "sin ganador";
        }

        if (best.Vendor == "Deepgram" || !IsBetter(best, deepgram))
        {
            return "sigue Deepgram";
        }

        return "gana " + best.Vendor + " " + best.Model;
    }

    private static bool DoneWhenMet(IReadOnlyList<VendorMeasurement> rows)
    {
        var deepgram = rows.FirstOrDefault(row => row.Vendor == "Deepgram");
        if (deepgram is null || deepgram.Status != "ok")
        {
            return false;
        }

        var measured = 0;
        foreach (var name in NewCandidates)
        {
            var row = rows.First(candidate => candidate.Vendor == name);
            if (row.Status == "ok")
            {
                measured++;
                continue;
            }

            if (row.Status != "sin clave")
            {
                return false;
            }
        }

        return measured >= 2;
    }

    private static bool IsProductionCandidate(VendorMeasurement row)
    {
        return row.Status == "ok" && row.Mode != "data" && row.Model != "flux-general-multi";
    }

    private static bool IsBetter(VendorMeasurement candidate, VendorMeasurement incumbent)
    {
        var entityDelta = DecisionEntity(candidate) - DecisionEntity(incumbent);
        if (entityDelta < -0.0000001)
        {
            return true;
        }

        if (entityDelta > 0.0000001)
        {
            return false;
        }

        return DecisionWord(candidate) < DecisionWord(incumbent) - 0.0000001;
    }

    private static double DecisionEntity(VendorMeasurement row)
    {
        if (row.ContextAccepted && row.WithContext.Count > 0)
        {
            return Scoring.CorpusEntityWordErrorRate(Scores(row.WithContext))
                ?? Scoring.CorpusWordErrorRate(Scores(row.WithContext));
        }

        return Scoring.CorpusEntityWordErrorRate(Scores(row.WithoutContext))
            ?? Scoring.CorpusWordErrorRate(Scores(row.WithoutContext));
    }

    private static double DecisionWord(VendorMeasurement row)
    {
        return Scoring.CorpusWordErrorRate(Scores(row.WithoutContext));
    }

    private static List<UtteranceScore> Scores(IReadOnlyList<PhraseMeasurement> phrases)
    {
        var scores = new List<UtteranceScore>(phrases.Count);
        foreach (var phrase in phrases)
        {
            scores.Add(phrase.Score);
        }

        return scores;
    }

    private static string RenderTable(IReadOnlyList<VendorMeasurement> rows, string decision, bool doneWhen)
    {
        var builder = new StringBuilder();
        builder.AppendLine("| Vendor | Modelo | Modo | WER crudo | WER acentos | WER sin diacríticos | Entidades sin contexto | Entidades con contexto | ms final | ms parcial | Notas |");
        builder.AppendLine("|---|---|---|---:|---:|---:|---:|---:|---:|---:|---|");
        foreach (var row in rows)
        {
            builder.Append("| ").Append(row.Vendor)
                .Append(" | ").Append(row.Model)
                .Append(" | ").Append(row.Mode)
                .Append(" | ").Append(FormatRatio(CorpusRate(row, static phrase => phrase.RawEdits, static phrase => phrase.RawReferenceWords)))
                .Append(" | ").Append(FormatRatio(CorpusRate(row, static phrase => phrase.Edits, static phrase => phrase.ReferenceWords)))
                .Append(" | ").Append(FormatRatio(CorpusRate(row, static phrase => phrase.StrippedEdits, static phrase => phrase.StrippedReferenceWords)))
                .Append(" | ").Append(FormatRatio(EntityRate(row.WithoutContext)))
                .Append(" | ").Append(row.ContextAccepted ? FormatRatio(EntityRate(row.WithContext)) : string.Empty)
                .Append(" | ").Append(FormatMean(row.WithoutContext, phrase => phrase.FinalMilliseconds))
                .Append(" | ").Append(row.Mode == "batch" || row.Mode == "data" ? string.Empty : FormatPartial(row))
                .Append(" | ").Append(row.Notes.Replace('|', '/'))
                .AppendLine(" |");
        }

        builder.AppendLine();
        builder.AppendLine("El ranking usa el WER normalizado con acentos.");
        builder.AppendLine("Decisión: " + decision + ".");
        builder.AppendLine(doneWhen
            ? "Done when: se puede cerrar con esta tabla."
            : "Done when: todavía no. Hace falta el control y al menos dos candidatos nuevos con número, o la fila «sin clave».");
        return builder.ToString();
    }

    private static double? WordRate(VendorMeasurement row)
        => CorpusRate(row, static phrase => phrase.Edits, static phrase => phrase.ReferenceWords);

    private static double? CorpusRate(
        VendorMeasurement row,
        Func<PhraseMeasurement, int> edits,
        Func<PhraseMeasurement, int> words)
    {
        if (row.Status != "ok")
        {
            return null;
        }

        var editCount = 0;
        var wordCount = 0;
        foreach (var phrase in row.WithoutContext)
        {
            editCount += edits(phrase);
            wordCount += words(phrase);
        }

        return wordCount == 0 ? 0 : (double)editCount / wordCount;
    }

    private static double? EntityRate(IReadOnlyList<PhraseMeasurement> phrases)
    {
        return phrases.Count == 0 ? null : Scoring.CorpusEntityWordErrorRate(Scores(phrases));
    }

    private static string FormatPartial(VendorMeasurement row)
    {
        var values = new List<int>();
        foreach (var phrase in row.WithoutContext)
        {
            if (phrase.FirstPartialMilliseconds is int milliseconds)
            {
                values.Add(milliseconds);
            }
        }

        if (values.Count == 0)
        {
            return string.Empty;
        }

        return ((int)values.Average()).ToString(CultureInfo.InvariantCulture);
    }

    private static string FormatMean(IReadOnlyList<PhraseMeasurement> phrases, Func<PhraseMeasurement, int> value)
    {
        if (phrases.Count == 0)
        {
            return string.Empty;
        }

        var sum = 0;
        foreach (var phrase in phrases)
        {
            sum += value(phrase);
        }

        return (sum / phrases.Count).ToString(CultureInfo.InvariantCulture);
    }

    private static string FormatRatio(double? ratio)
    {
        return ratio is null ? string.Empty : ratio.Value.ToString("0.000", CultureInfo.InvariantCulture);
    }

    private static string RenderJson(
        string sampleDir,
        IReadOnlyList<WavClip> clips,
        IReadOnlyList<VendorMeasurement> rows,
        string decision,
        bool doneWhen)
    {
        var phrases = clips.Select(clip => new
        {
            clip.Id,
            clip.Phrase,
            durationSeconds = Math.Round(clip.DurationSeconds, 3),
        });
        var vendors = rows.Select(row => new
        {
            row.Vendor,
            row.Model,
            row.Mode,
            row.Status,
            wer = WordRate(row),
            werRaw = CorpusRate(row, static phrase => phrase.RawEdits, static phrase => phrase.RawReferenceWords),
            werAccent = WordRate(row),
            werStripped = CorpusRate(row, static phrase => phrase.StrippedEdits, static phrase => phrase.StrippedReferenceWords),
            meanPhraseWer = row.Status == "ok" ? Scoring.MeanPhraseWordErrorRate(Scores(row.WithoutContext)) : (double?)null,
            entityWerWithoutContext = EntityRate(row.WithoutContext),
            entityWerWithContext = row.ContextAccepted ? EntityRate(row.WithContext) : null,
            row.Notes,
            decisionEntity = row.Status == "ok" ? DecisionEntity(row) : (double?)null,
            withoutContext = row.WithoutContext,
            withContext = row.ContextAccepted ? row.WithContext : null,
        });
        var payload = new
        {
            sampleDir,
            decision,
            doneWhen,
            phrases,
            vendors,
        };
        return JsonSerializer.Serialize(payload, JsonOptions);
    }
}
