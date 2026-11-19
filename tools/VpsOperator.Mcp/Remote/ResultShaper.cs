using System.Text.Json.Nodes;

namespace VpsOperator.Mcp;

internal static class ResultShaper
{
    public static string Shape(string stdout, IReadOnlyList<string> fields)
    {
        if (JsonNode.Parse(stdout) is not JsonObject root)
        {
            throw new FormatException("The remote result was not a JSON object.");
        }

        var shaped = new JsonObject();
        foreach (var field in fields)
        {
            if (root[field] is JsonNode value)
            {
                shaped[field] = value.DeepClone();
            }
        }

        return shaped.ToJsonString();
    }

    public static string? TryShape(string stdout, IReadOnlyList<string> fields)
    {
        try
        {
            return Shape(stdout, fields);
        }
        catch (FormatException)
        {
            return null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
