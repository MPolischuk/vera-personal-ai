using System.Text.Json;
using Assistant.Abstractions.Tools;

namespace Assistant.Extensions.Notes.Tools;

internal static class NoteArguments
{
    public static bool HasProperty(ToolArguments args, string name)
    {
        using var document = JsonDocument.Parse(args.RawJson);
        return document.RootElement.ValueKind is JsonValueKind.Object
            && document.RootElement.TryGetProperty(name, out _);
    }

    public static bool TryReadOptionalBool(
        ToolArguments args, string name, out bool? value, out string error)
    {
        using var document = JsonDocument.Parse(args.RawJson);
        if (document.RootElement.ValueKind is not JsonValueKind.Object
            || !document.RootElement.TryGetProperty(name, out var property))
        {
            value = null;
            error = "";
            return true;
        }

        if (property.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            value = property.GetBoolean();
            error = "";
            return true;
        }

        value = null;
        error = $"El parámetro '{name}' tiene que ser true o false.";
        return false;
    }

    public static bool TryReadGuids(
        ToolArguments args, string name, bool required, out IReadOnlyList<Guid> ids, out string error)
    {
        using var document = JsonDocument.Parse(args.RawJson);
        if (document.RootElement.ValueKind is not JsonValueKind.Object
            || !document.RootElement.TryGetProperty(name, out var property))
        {
            ids = [];
            if (required)
            {
                error = $"Falta el parámetro obligatorio '{name}'.";
                return false;
            }

            error = "";
            return true;
        }

        if (property.ValueKind is not JsonValueKind.Array)
        {
            ids = [];
            error = $"El parámetro '{name}' tiene que ser una lista de uuid.";
            return false;
        }

        var parsed = new List<Guid>();
        foreach (var item in property.EnumerateArray())
        {
            if (item.ValueKind is not JsonValueKind.String || !Guid.TryParse(item.GetString(), out var id))
            {
                ids = [];
                error = $"El parámetro '{name}' tiene que ser una lista de uuid.";
                return false;
            }

            parsed.Add(id);
        }

        ids = parsed;
        error = "";
        return true;
    }

    public static bool TryReadOptionalIndexes(ToolArguments args, out IReadOnlyList<int>? indexes, out string error)
    {
        using var document = JsonDocument.Parse(args.RawJson);
        if (document.RootElement.ValueKind is not JsonValueKind.Object
            || !document.RootElement.TryGetProperty("claim_indexes", out var property)
            || property.ValueKind is JsonValueKind.Null)
        {
            indexes = null;
            error = "";
            return true;
        }

        if (property.ValueKind is not JsonValueKind.Array)
        {
            indexes = null;
            error = "El parámetro 'claim_indexes' tiene que ser una lista de enteros.";
            return false;
        }

        var parsed = new List<int>();
        foreach (var item in property.EnumerateArray())
        {
            if (!item.TryGetInt32(out var index))
            {
                indexes = null;
                error = "El parámetro 'claim_indexes' tiene que ser una lista de enteros.";
                return false;
            }

            parsed.Add(index);
        }

        indexes = parsed;
        error = "";
        return true;
    }
}
