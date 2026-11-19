using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Assistant.Mcp.Coolify;

internal static class CoolifyToolCatalog
{
    public static IReadOnlyList<McpServerTool> Create(CoolifyAllowlist allowlist)
    {
        var tools = new List<McpServerTool>();
        foreach (var method in typeof(CoolifyTools).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
        {
            if (method.GetCustomAttribute<McpServerToolAttribute>() is null)
            {
                continue;
            }

            var tool = McpServerTool.Create(
                method,
                static context => ActivatorUtilities.CreateInstance<CoolifyTools>(
                    context.Services ?? throw new InvalidOperationException("MCP request services are required.")),
                new McpServerToolCreateOptions());
            tool.ProtocolTool.InputSchema = WithEnums(tool.ProtocolTool.InputSchema, allowlist.Names);
            tools.Add(tool);
        }

        return tools;
    }

    internal static JsonElement WithEnums(JsonElement schema, IReadOnlyCollection<string> appNames)
    {
        if (JsonNode.Parse(schema.GetRawText()) is not JsonObject root
            || root["properties"] is not JsonObject properties)
        {
            return schema;
        }

        if (properties["app"] is JsonObject app && appNames.Count > 0)
        {
            var values = new JsonArray();
            foreach (var name in appNames)
            {
                values.Add(name);
            }

            app["enum"] = values;
            app["description"] = "Nombres permitidos: " + string.Join(", ", appNames) + ".";
        }

        if (properties["action"] is JsonObject action)
        {
            action["enum"] = new JsonArray("start", "stop", "restart", "deploy");
        }

        return JsonSerializer.SerializeToElement(root);
    }
}
