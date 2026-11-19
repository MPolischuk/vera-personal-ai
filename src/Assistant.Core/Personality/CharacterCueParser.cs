using System.Text;
using Assistant.Core.Conversations;

namespace Assistant.Core.Personality;

internal enum CueParseOutcome
{
    Parsed,
    Invalid,
    Absent,
}

/// <summary>
/// Pure streaming parser for a leading <c>[[cue:…]]</c> preamble (D2–D3).
/// No avatar references; vocabulary stays in Core.
/// </summary>
internal sealed class CharacterCueParser
{
    public const int MaxPreambleChars = 48;

    // Keep in sync with AvatarCues expressions (+ "none").
    internal static readonly string[] ExpressionVocabulary =
    [
        "happy",
        "angry",
        "sad",
        "relaxed",
        "surprised",
        "neutral",
        "none",
    ];

    internal static readonly string[] GestureVocabulary =
    [
        "nod",
        "shake",
        "wave",
        "shrug",
        "none",
    ];

    private static readonly HashSet<string> Expressions = new(ExpressionVocabulary, StringComparer.Ordinal);
    private static readonly HashSet<string> Gestures = new(GestureVocabulary, StringComparer.Ordinal);

    private readonly StringBuilder _buffer = new();
    private bool _resolved;
    private bool _cueAccepted;

    public CueParseOutcome? Outcome { get; private set; }

    /// <summary>Expression when <see cref="Outcome"/> is <see cref="CueParseOutcome.Parsed"/>.</summary>
    public string? ResolvedExpression { get; private set; }

    /// <summary>First 80 characters pushed, for live diagnosis when the model skips the preamble.</summary>
    public string RawHead { get; private set; } = "";

    public IReadOnlyList<TurnUpdate> Push(string chunk)
    {
        if (chunk.Length == 0)
        {
            return [];
        }

        if (RawHead.Length < 80)
        {
            var take = Math.Min(chunk.Length, 80 - RawHead.Length);
            RawHead += chunk[..take];
        }

        if (_resolved)
        {
            return [new TurnToken(chunk)];
        }

        _buffer.Append(chunk);
        return TryResolveBuffered();
    }

    /// <summary>
    /// Tool-only generates Flush empty. PT-24 D1: stay unresolved so the
    /// next round can still open with <c>[[cue:…]]</c>. After a cue was
    /// already decided, look again to strip the copy the model repeats.
    /// </summary>
    public void BeginRound()
    {
        if (!_resolved)
        {
            return;
        }

        _resolved = false;
        _buffer.Clear();
    }

    public IReadOnlyList<TurnUpdate> Flush()
    {
        if (_resolved)
        {
            return [];
        }

        if (_buffer.Length == 0)
        {
            return [];
        }

        var candidate = _buffer.ToString();
        _buffer.Clear();
        return ResolveCandidate(candidate, rest: string.Empty, newlineTerminated: false);
    }

    private List<TurnUpdate> TryResolveBuffered()
    {
        while (true)
        {
            var text = _buffer.ToString();
            var newline = text.IndexOf('\n');
            if (newline >= 0)
            {
                var line = text[..newline];
                var rest = text[(newline + 1)..];
                if (IsSkippablePreambleLine(TrimCueLine(line)))
                {
                    _buffer.Clear();
                    _buffer.Append(rest);
                    continue;
                }

                _buffer.Clear();
                return ResolveCandidate(line, rest, newlineTerminated: true);
            }

            if (text.Length >= MaxPreambleChars)
            {
                var preamble = text[..MaxPreambleChars];
                var rest = text[MaxPreambleChars..];
                _buffer.Clear();
                return ResolveCandidate(preamble, rest, newlineTerminated: false);
            }

            return [];
        }
    }

    private List<TurnUpdate> ResolveCandidate(
        string candidate,
        string rest,
        bool newlineTerminated)
    {
        if (newlineTerminated)
        {
            return ResolveExactLine(candidate, rest);
        }

        return ResolvePrefixOrAbsent(candidate, rest);
    }

    private List<TurnUpdate> ResolveExactLine(string line, string rest)
    {
        var trimmed = TrimCueLine(line);
        if (TryParseExactCue(trimmed, out var expression, out var gesture, out var formMatched))
        {
            return FinishCue(expression, gesture, formMatched, rest);
        }

        return FinishAbsent(line + "\n" + rest);
    }

    private List<TurnUpdate> ResolvePrefixOrAbsent(string candidate, string rest)
    {
        if (TryParsePrefixCue(candidate, out var expression, out var gesture, out var formMatched, out var consumed))
        {
            var afterCue = candidate[consumed..] + rest;
            return FinishCue(expression, gesture, formMatched, afterCue);
        }

        if (IsSkippablePreambleLine(TrimCueLine(candidate)) && rest.Length == 0)
        {
            return [];
        }

        return FinishAbsent(candidate + rest);
    }

    private List<TurnUpdate> FinishCue(
        string expression,
        string gesture,
        bool formMatched,
        string rest)
    {
        _resolved = true;
        var updates = new List<TurnUpdate>();

        if (formMatched && Expressions.Contains(expression) && Gestures.Contains(gesture))
        {
            if (!_cueAccepted)
            {
                _cueAccepted = true;
                Outcome = CueParseOutcome.Parsed;
                ResolvedExpression = expression;
                updates.Add(new TurnCharacterCue(expression, gesture));
            }
        }
        else if (!_cueAccepted)
        {
            Outcome = CueParseOutcome.Invalid;
        }

        if (rest.Length > 0)
        {
            updates.Add(new TurnToken(rest));
        }

        return updates;
    }

    private List<TurnUpdate> FinishAbsent(string text)
    {
        _resolved = true;
        Outcome ??= CueParseOutcome.Absent;
        if (text.Length == 0)
        {
            return [];
        }

        return [new TurnToken(text)];
    }

    /// <summary>
    /// Live models copy the example filler "Dale." or open with blank lines
    /// before the cue. Those are not the reply; keep looking for the preamble.
    /// </summary>
    private static bool IsSkippablePreambleLine(string trimmed)
        => trimmed.Length == 0
           || trimmed.Equals("Dale.", StringComparison.Ordinal)
           || trimmed.Equals("Dale", StringComparison.Ordinal);

    private static string TrimCueLine(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.EndsWith('\r'))
        {
            trimmed = trimmed[..^1].TrimEnd();
        }

        return trimmed;
    }

    private static bool TryParseExactCue(
        string trimmedLine,
        out string expression,
        out string gesture,
        out bool formMatched)
    {
        expression = "none";
        gesture = "none";
        formMatched = false;

        if (!trimmedLine.StartsWith("[[cue:", StringComparison.Ordinal)
            || !trimmedLine.EndsWith("]]", StringComparison.Ordinal))
        {
            return false;
        }

        formMatched = true;
        return TrySplitCueBody(trimmedLine["[[cue:".Length..^2], out expression, out gesture);
    }

    private static bool TryParsePrefixCue(
        string candidate,
        out string expression,
        out string gesture,
        out bool formMatched,
        out int consumed)
    {
        expression = "none";
        gesture = "none";
        formMatched = false;
        consumed = 0;

        if (!candidate.StartsWith("[[cue:", StringComparison.Ordinal))
        {
            return false;
        }

        var close = candidate.IndexOf("]]", "[[cue:".Length, StringComparison.Ordinal);
        if (close < 0)
        {
            return false;
        }

        formMatched = true;
        consumed = close + 2;
        var body = candidate["[[cue:".Length..close];
        TrySplitCueBody(body, out expression, out gesture);
        return true;
    }

    private static bool TrySplitCueBody(string body, out string expression, out string gesture)
    {
        expression = "none";
        gesture = "none";

        var pipe = body.IndexOf('|');
        if (pipe < 0)
        {
            expression = body;
            return true;
        }

        expression = body[..pipe];
        gesture = body[(pipe + 1)..];
        return true;
    }
}
