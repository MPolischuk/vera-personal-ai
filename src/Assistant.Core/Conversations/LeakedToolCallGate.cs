using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Assistant.Core.Conversations;

/// <summary>
/// Holds streamed assistant text that looks like a dumped tool call (JSON in the
/// message body) so it never reaches the user or TTS. Recovers a real
/// <see cref="FunctionCallContent"/> when the name is in the turn's catalog.
/// </summary>
internal sealed class LeakedToolCallGate(IReadOnlySet<string> toolNames)
{
    private readonly StringBuilder _hold = new();
    private bool _passThrough;
    private bool _dropRemainder;
    private readonly List<RecoveredToolCall> _calls = [];

    public IReadOnlyList<RecoveredToolCall> RecoveredCalls => _calls;

    public IEnumerable<string> Push(string chunk)
    {
        if (string.IsNullOrEmpty(chunk) || _dropRemainder)
        {
            return [];
        }

        if (_passThrough)
        {
            return [chunk];
        }

        _hold.Append(chunk);
        return Drain(flush: false);
    }

    public IEnumerable<string> Flush()
        => _dropRemainder ? [] : Drain(flush: true);

    private List<string> Drain(bool flush)
    {
        List<string>? visible = null;

        while (_hold.Length > 0 && !_dropRemainder)
        {
            if (_passThrough)
            {
                visible ??= [];
                visible.Add(_hold.ToString());
                _hold.Clear();
                break;
            }

            var snapshot = _hold.ToString();
            switch (LeakedToolCallParser.Inspect(snapshot, toolNames, flush))
            {
                case { Kind: LeakedToolCallKind.Incomplete }:
                    if (_hold.Length > LeakedToolCallParser.MaxHoldChars)
                    {
                        _passThrough = true;
                        continue;
                    }

                    return visible ?? [];

                case { Kind: LeakedToolCallKind.NotATool }:
                    _passThrough = true;
                    continue;

                case { Kind: LeakedToolCallKind.ToolCall, Call: { } recovered, Consumed: var consumed }:
                    _calls.Add(recovered);
                    _hold.Remove(0, consumed);
                    _dropRemainder = true;
                    _hold.Clear();
                    return visible ?? [];
            }
        }

        return visible ?? [];
    }
}

internal readonly record struct RecoveredToolCall(
    string Name,
    IDictionary<string, object?> Arguments);

internal enum LeakedToolCallKind
{
    Incomplete,
    NotATool,
    ToolCall,
}

internal readonly record struct LeakedToolCallInspection(
    LeakedToolCallKind Kind,
    RecoveredToolCall? Call = null,
    int Consumed = 0);

internal static class LeakedToolCallParser
{
    internal const int MaxHoldChars = 8192;

    public static LeakedToolCallInspection Inspect(
        string buffer,
        IReadOnlySet<string> toolNames,
        bool flush)
    {
        if (toolNames.Count == 0)
        {
            return new(LeakedToolCallKind.NotATool);
        }

        var span = buffer.AsSpan();
        var i = SkipWs(span, 0);
        if (i >= span.Length)
        {
            return flush ? new(LeakedToolCallKind.NotATool) : new(LeakedToolCallKind.Incomplete);
        }

        if (span[i] != '{')
        {
            return new(LeakedToolCallKind.NotATool);
        }

        var objectEnd = FindJsonObjectEnd(span, i);
        if (objectEnd >= 0)
        {
            var json = buffer[i..(objectEnd + 1)];
            if (TryParseObject(json, toolNames, out var call))
            {
                return new(LeakedToolCallKind.ToolCall, call, objectEnd + 1);
            }

            return new(LeakedToolCallKind.NotATool);
        }

        if (TryMatchMalformedHeader(span, toolNames, out var name, out var afterHeader))
        {
            var prose = FindProseStart(span, afterHeader, toolNames);
            if (prose < 0)
            {
                return flush
                    ? new(LeakedToolCallKind.ToolCall, new RecoveredToolCall(name, EmptyArgs()), span.Length)
                    : new(LeakedToolCallKind.Incomplete);
            }

            return new(
                LeakedToolCallKind.ToolCall,
                new RecoveredToolCall(name, EmptyArgs()),
                prose);
        }

        if (flush || buffer.Length >= MaxHoldChars)
        {
            return new(LeakedToolCallKind.NotATool);
        }

        return HeaderLooksIncomplete(span[i..])
            ? new(LeakedToolCallKind.Incomplete)
            : new(LeakedToolCallKind.NotATool);
    }

    public static ChatResponse AttachRecoveredCalls(ChatResponse response, IReadOnlyList<RecoveredToolCall> calls)
    {
        if (calls.Count == 0)
        {
            return response;
        }

        if (response.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>().Any())
        {
            return response;
        }

        List<AIContent> contents = [.. calls.Select(c =>
            (AIContent)new FunctionCallContent(
                Guid.CreateVersion7().ToString("N"),
                c.Name,
                c.Arguments))];

        var messages = response.Messages.ToList();
        var assistant = new ChatMessage(ChatRole.Assistant, contents);
        var index = messages.FindLastIndex(m => m.Role == ChatRole.Assistant);
        if (index >= 0)
        {
            messages[index] = assistant;
        }
        else
        {
            messages.Add(assistant);
        }

        return new ChatResponse(messages)
        {
            ModelId = response.ModelId,
            Usage = response.Usage,
        };
    }

    private static bool TryParseObject(
        string json,
        IReadOnlySet<string> toolNames,
        out RecoveredToolCall call)
    {
        call = default;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("name", out var nameEl)
                || nameEl.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            var name = nameEl.GetString();
            if (string.IsNullOrEmpty(name) || !toolNames.Contains(name))
            {
                return false;
            }

            call = new RecoveredToolCall(name, ReadArgs(root));
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static Dictionary<string, object?> ReadArgs(JsonElement root)
    {
        if (!root.TryGetProperty("parameters", out var args)
            && !root.TryGetProperty("arguments", out args))
        {
            return EmptyArgs();
        }

        if (args.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
            || args.ValueKind != JsonValueKind.Object)
        {
            return EmptyArgs();
        }

        return args.EnumerateObject()
            .ToDictionary(p => p.Name, p => (object?)p.Value.Clone(), StringComparer.Ordinal);
    }

    private static Dictionary<string, object?> EmptyArgs()
        => new(StringComparer.Ordinal);

    private static bool TryMatchMalformedHeader(
        ReadOnlySpan<char> text,
        IReadOnlySet<string> toolNames,
        out string name,
        out int afterHeader)
    {
        name = "";
        afterHeader = 0;
        var i = SkipWs(text, 0);
        if (i >= text.Length || text[i] != '{')
        {
            return false;
        }

        i = SkipWs(text, i + 1);
        if (!TryConsumeString(text, ref i, out var key) || key != "name")
        {
            return false;
        }

        i = SkipWs(text, i);
        if (i >= text.Length || text[i] != ':')
        {
            return false;
        }

        i = SkipWs(text, i + 1);
        if (!TryConsumeString(text, ref i, out name) || !toolNames.Contains(name))
        {
            return false;
        }

        i = SkipWs(text, i);
        if (i < text.Length && text[i] == ',')
        {
            i = SkipWs(text, i + 1);
            if (TryConsumeString(text, ref i, out var argsKey)
                && (argsKey is "parameters" or "arguments"))
            {
                i = SkipWs(text, i);
                if (i < text.Length && text[i] == ':')
                {
                    i = SkipWs(text, i + 1);
                }
            }
        }

        afterHeader = i;
        return true;
    }

    private static int FindProseStart(
        ReadOnlySpan<char> text,
        int from,
        IReadOnlySet<string> toolNames)
    {
        var i = from;
        while (i < text.Length)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c) || c is '{' or '}' or '[' or ']' or ':' or ',' or '"' or '\\')
            {
                i++;
                continue;
            }

            if (IsKeywordThenLetter(text, i, "null", out var afterNull))
            {
                return afterNull;
            }

            if (IsKeywordThenLetter(text, i, "true", out var afterTrue))
            {
                return afterTrue;
            }

            if (IsKeywordThenLetter(text, i, "false", out var afterFalse))
            {
                return afterFalse;
            }

            if (char.IsLetter(c) || c == '_')
            {
                var end = i + 1;
                while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] == '_'))
                {
                    end++;
                }

                var ident = text[i..end].ToString();
                if (ident is "null" or "true" or "false" or "name" or "parameters" or "arguments"
                    || toolNames.Contains(ident))
                {
                    i = end;
                    continue;
                }

                return i;
            }

            i++;
        }

        return -1;
    }

    private static bool IsKeywordThenLetter(
        ReadOnlySpan<char> text, int i, string keyword, out int afterKeyword)
    {
        afterKeyword = i + keyword.Length;
        if (afterKeyword > text.Length)
        {
            return false;
        }

        if (!text[i..afterKeyword].Equals(keyword, StringComparison.Ordinal))
        {
            return false;
        }

        return afterKeyword < text.Length && char.IsLetter(text[afterKeyword]);
    }

    private static bool HeaderLooksIncomplete(ReadOnlySpan<char> fromBrace)
    {
        // Still assembling {"name": "... — do not leak a half object.
        var depth = 0;
        var inString = false;
        var escape = false;
        foreach (var c in fromBrace)
        {
            if (inString)
            {
                if (escape)
                {
                    escape = false;
                    continue;
                }

                if (c == '\\')
                {
                    escape = true;
                    continue;
                }

                if (c == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (c == '"')
            {
                inString = true;
                continue;
            }

            if (c == '{')
            {
                depth++;
            }
            else if (c == '}')
            {
                depth--;
            }
        }

        return inString || depth > 0;
    }

    private static int FindJsonObjectEnd(ReadOnlySpan<char> text, int start)
    {
        var depth = 0;
        var inString = false;
        var escape = false;
        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];
            if (inString)
            {
                if (escape)
                {
                    escape = false;
                    continue;
                }

                if (c == '\\')
                {
                    escape = true;
                    continue;
                }

                if (c == '"')
                {
                    inString = false;
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    break;
                case '{':
                    depth++;
                    break;
                case '}':
                    depth--;
                    if (depth == 0)
                    {
                        return i;
                    }

                    break;
            }
        }

        return -1;
    }

    private static bool TryConsumeString(ReadOnlySpan<char> text, ref int i, out string value)
    {
        value = "";
        if (i >= text.Length || text[i] != '"')
        {
            return false;
        }

        i++;
        var start = i;
        var escape = false;
        for (; i < text.Length; i++)
        {
            var c = text[i];
            if (escape)
            {
                escape = false;
                continue;
            }

            if (c == '\\')
            {
                escape = true;
                continue;
            }

            if (c == '"')
            {
                value = text[start..i].ToString();
                i++;
                return true;
            }
        }

        return false;
    }

    private static int SkipWs(ReadOnlySpan<char> text, int i)
    {
        while (i < text.Length && char.IsWhiteSpace(text[i]))
        {
            i++;
        }

        return i;
    }
}
