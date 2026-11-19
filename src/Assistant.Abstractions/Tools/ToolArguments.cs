using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Assistant.Abstractions.Tools;

public sealed class ToolArguments
{
    private readonly JsonElement _raw;

    private ToolArguments(JsonElement raw) => _raw = raw;

    public static ToolArguments Empty { get; } = FromJson("{}");

    /// <summary>
    /// The <see cref="JsonElement"/> is cloned on purpose: the one from the model may
    /// be backed by a JsonDocument that is disposed before the tool runs, and then
    /// access throws ObjectDisposedException far from the cause.
    /// </summary>
    public static ToolArguments From(JsonElement raw) => new(raw.Clone());

    public static ToolArguments FromJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        return new ToolArguments(document.RootElement.Clone());
    }

    /// <summary>Exact shape audited in <c>tool_invocations.arguments</c>.</summary>
    public string RawJson => _raw.GetRawText();

    public bool TryGetString(string name, [NotNullWhen(true)] out string? value)
    {
        if (_raw.ValueKind is JsonValueKind.Object
            && _raw.TryGetProperty(name, out var property)
            && property.ValueKind is JsonValueKind.String)
        {
            value = property.GetString();
            return value is not null;
        }

        value = null;
        return false;
    }

    public bool TryGetInt32(string name, out int value)
    {
        if (_raw.ValueKind is JsonValueKind.Object
            && _raw.TryGetProperty(name, out var property)
            && property.TryGetInt32(out value))
        {
            return true;
        }

        value = 0;
        return false;
    }

    public T? Deserialize<T>(JsonSerializerOptions? options = null)
        => _raw.Deserialize<T>(options);
}

public sealed record ToolResult(
    ToolStatus Status, string? Content, string? Error, OutputTrust? OutputTrust = null)
{
    public static ToolResult Ok(string content) => new(ToolStatus.Ok, content, null);

    public static ToolResult Ok(string content, OutputTrust outputTrust)
        => new(ToolStatus.Ok, content, null, outputTrust);

    public static ToolResult Failed(string error) => new(ToolStatus.Failed, null, error);
}

public enum ToolStatus { Ok, Failed }
