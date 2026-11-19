using System.Text;

namespace Assistant.Core.Speech;

/// <summary>
/// Pure sentence accumulator for TTS chunking. One instance per generation stream; not thread-safe.
/// </summary>
public sealed class SentenceSegmenter
{
    private readonly SentenceSegmenterOptions _options;
    private readonly StringBuilder _buffer = new();
    private int _nextIndex;

    public SentenceSegmenter(SentenceSegmenterOptions? options = null)
    {
        _options = options ?? new SentenceSegmenterOptions();
        if (_options.FirstMinChars > _options.FirstMaxChars)
        {
            throw new ArgumentException("FirstMinChars must be ≤ FirstMaxChars.");
        }

        if (_options.SubsequentMinChars > _options.SubsequentMaxChars)
        {
            throw new ArgumentException("SubsequentMinChars must be ≤ SubsequentMaxChars.");
        }
    }

    public IReadOnlyList<SegmentedSentence> Push(string? chunk)
    {
        if (string.IsNullOrWhiteSpace(chunk))
        {
            return [];
        }

        AppendNormalized(chunk);
        return Drain(flush: false);
    }

    public IReadOnlyList<SegmentedSentence> Flush()
        => Drain(flush: true);

    private List<SegmentedSentence> Drain(bool flush)
    {
        List<SegmentedSentence>? emitted = null;

        while (_buffer.Length > 0)
        {
            if (!TryFindCut(flush, out var contentEnd, out var consumeEnd))
            {
                break;
            }

            var text = NormalizeForEmit(_buffer, 0, contentEnd);
            _buffer.Remove(0, consumeEnd);

            if (text.Length == 0)
            {
                continue;
            }

            emitted ??= [];
            emitted.Add(new SegmentedSentence(_nextIndex++, text));
        }

        return emitted ?? [];
    }

    private bool TryFindCut(bool flush, out int contentEnd, out int consumeEnd)
    {
        int minChars = _nextIndex == 0 ? _options.FirstMinChars : _options.SubsequentMinChars;
        int maxChars = _nextIndex == 0 ? _options.FirstMaxChars : _options.SubsequentMaxChars;

        int searchFrom = 0;
        while (TryFindBoundary(_buffer, searchFrom, flush, out contentEnd, out consumeEnd))
        {
            var candidate = NormalizeForEmit(_buffer, 0, contentEnd);
            if (candidate.Length == 0)
            {
                // Drop leading junk / bare newlines and keep searching.
                _buffer.Remove(0, consumeEnd);
                searchFrom = 0;
                continue;
            }

            if (candidate.Length >= minChars)
            {
                return true;
            }

            // Min not met: merge past this boundary (Flush only ignores min for the final remainder).
            searchFrom = consumeEnd;
        }

        if (_buffer.Length >= maxChars && TryMaxSplit(maxChars, out contentEnd, out consumeEnd))
        {
            return true;
        }

        if (flush)
        {
            var rest = NormalizeForEmit(_buffer, 0, _buffer.Length);
            if (rest.Length > 0)
            {
                contentEnd = _buffer.Length;
                consumeEnd = _buffer.Length;
                return true;
            }

            _buffer.Clear();
        }

        contentEnd = 0;
        consumeEnd = 0;
        return false;
    }

    private static bool TryFindBoundary(
        StringBuilder buffer, int searchFrom, bool flush, out int contentEnd, out int consumeEnd)
    {
        contentEnd = 0;
        consumeEnd = 0;
        if (buffer.Length == 0 || searchFrom >= buffer.Length)
        {
            return false;
        }

        for (int i = searchFrom; i < buffer.Length; i++)
        {
            // Double newline is a hard boundary.
            if (buffer[i] == '\n' && i + 1 < buffer.Length && buffer[i + 1] == '\n')
            {
                contentEnd = i;
                consumeEnd = i + 2;
                while (consumeEnd < buffer.Length && buffer[consumeEnd] == '\n')
                {
                    consumeEnd++;
                }

                return true;
            }

            if (!TryReadTerminator(buffer, i, out var termEnd))
            {
                continue;
            }

            if (IsFalsePositivePeriod(buffer, i, termEnd))
            {
                i = termEnd - 1;
                continue;
            }

            int after = termEnd;
            while (after < buffer.Length && IsClosingQuote(buffer[after]))
            {
                after++;
            }

            if (IsConfirmedBoundary(buffer, after, flush))
            {
                contentEnd = after;
                consumeEnd = SkipWhitespace(buffer, after);
                return true;
            }

            i = termEnd - 1;
        }

        return false;
    }

    private static bool TryReadTerminator(StringBuilder buffer, int index, out int endExclusive)
    {
        char c = buffer[index];
        if (c == '…')
        {
            endExclusive = index + 1;
            return true;
        }

        if (c == '?' || c == '!')
        {
            endExclusive = index + 1;
            return true;
        }

        if (c == '.')
        {
            // Prefer a complete "..."; do not cut on a partial ellipsis mid-stream.
            if (index + 2 < buffer.Length && buffer[index + 1] == '.' && buffer[index + 2] == '.')
            {
                endExclusive = index + 3;
                return true;
            }

            if (index + 1 < buffer.Length && buffer[index + 1] == '.')
            {
                // Incomplete "...": wait for the third dot.
                endExclusive = index;
                return false;
            }

            endExclusive = index + 1;
            return true;
        }

        endExclusive = index;
        return false;
    }

    private static bool IsFalsePositivePeriod(StringBuilder buffer, int periodIndex, int termEnd)
    {
        // Only single '.' false positives; '...' / '…' / ?! are real terminators.
        if (termEnd - periodIndex != 1 || buffer[periodIndex] != '.')
        {
            return false;
        }

        if (IsDecimalPeriod(buffer, periodIndex))
        {
            return true;
        }

        if (IsListItemPeriod(buffer, periodIndex))
        {
            return true;
        }

        if (IsAbbreviationPeriod(buffer, periodIndex))
        {
            return true;
        }

        if (IsInitialPeriod(buffer, periodIndex))
        {
            return true;
        }

        return false;
    }

    private static bool IsDecimalPeriod(StringBuilder buffer, int periodIndex)
        => periodIndex > 0
           && char.IsDigit(buffer[periodIndex - 1])
           && periodIndex + 1 < buffer.Length
           && char.IsDigit(buffer[periodIndex + 1]);

    private static bool IsListItemPeriod(StringBuilder buffer, int periodIndex)
    {
        if (periodIndex + 1 >= buffer.Length || !char.IsWhiteSpace(buffer[periodIndex + 1]))
        {
            return false;
        }

        int j = periodIndex - 1;
        if (j < 0 || !char.IsDigit(buffer[j]))
        {
            return false;
        }

        while (j >= 0 && char.IsDigit(buffer[j]))
        {
            j--;
        }

        return j < 0 || buffer[j] == '\n';
    }

    private static bool IsAbbreviationPeriod(StringBuilder buffer, int periodIndex)
    {
        int start = periodIndex - 1;
        while (start >= 0 && !char.IsWhiteSpace(buffer[start]))
        {
            start--;
        }

        start++;

        if (start >= periodIndex)
        {
            return false;
        }

        var token = buffer.ToString(start, periodIndex - start);
        return SpanishSentenceAbbreviations.Values.Contains(token);
    }

    private static bool IsInitialPeriod(StringBuilder buffer, int periodIndex)
    {
        if (periodIndex == 0)
        {
            return false;
        }

        char letter = buffer[periodIndex - 1];
        if (!char.IsUpper(letter))
        {
            return false;
        }

        return periodIndex == 1 || char.IsWhiteSpace(buffer[periodIndex - 2]);
    }

    private static bool IsConfirmedBoundary(StringBuilder buffer, int afterTerminator, bool flush)
    {
        if (afterTerminator >= buffer.Length)
        {
            return flush;
        }

        int i = afterTerminator;
        if (!char.IsWhiteSpace(buffer[i]))
        {
            return false;
        }

        i = SkipWhitespace(buffer, i);
        if (i >= buffer.Length)
        {
            return flush;
        }

        char next = buffer[i];
        return char.IsUpper(next) || next == '¿' || next == '¡';
    }

    private bool TryMaxSplit(int maxChars, out int contentEnd, out int consumeEnd)
    {
        int limit = Math.Min(maxChars, _buffer.Length);
        for (int i = limit; i > 0; i--)
        {
            if (!char.IsWhiteSpace(_buffer[i - 1]))
            {
                continue;
            }

            int end = i - 1;
            while (end > 0 && char.IsWhiteSpace(_buffer[end - 1]))
            {
                end--;
            }

            if (end == 0)
            {
                continue;
            }

            contentEnd = end;
            consumeEnd = SkipWhitespace(_buffer, end);
            return true;
        }

        // No whitespace: hard cut at max.
        contentEnd = limit;
        consumeEnd = limit;
        return limit > 0;
    }

    private void AppendNormalized(string chunk)
    {
        for (int i = 0; i < chunk.Length; i++)
        {
            char c = chunk[i];
            if (c == '\r')
            {
                if (i + 1 < chunk.Length && chunk[i + 1] == '\n')
                {
                    continue;
                }

                _buffer.Append('\n');
                continue;
            }

            _buffer.Append(c);
        }
    }

    private static int SkipWhitespace(StringBuilder buffer, int index)
    {
        while (index < buffer.Length && char.IsWhiteSpace(buffer[index]))
        {
            index++;
        }

        return index;
    }

    private static bool IsClosingQuote(char c) => c is '"' or '\'' or '»';

    private static string NormalizeForEmit(StringBuilder buffer, int start, int endExclusive)
    {
        if (endExclusive <= start)
        {
            return string.Empty;
        }

        var raw = buffer.ToString(start, endExclusive - start).Trim();
        if (raw.Length == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder(raw.Length);
        bool pendingSpace = false;
        foreach (char c in raw)
        {
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = sb.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                sb.Append(' ');
                pendingSpace = false;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }
}
