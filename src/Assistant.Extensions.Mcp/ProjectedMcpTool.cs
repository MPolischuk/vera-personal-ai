using System.Text;
using System.Text.Json;
using Assistant.Abstractions.Research;
using Assistant.Abstractions.Tools;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Extensions.Tasks;
using ModelContextProtocol.Protocol;

namespace Assistant.Extensions.Mcp;

internal sealed class ProjectedMcpTool : ITool
{
    private static readonly JsonElement EmptySchema = JsonDocument.Parse("{}").RootElement.Clone();

    private readonly McpSessionHost.ServerSession _session;
    private readonly string _remoteName;
    private readonly McpTaskPoller _poller;
    private readonly bool _metered;
    private readonly IServiceProvider? _services;

    public ProjectedMcpTool(
        McpSessionHost.ServerSession session,
        string catalogName,
        string description,
        JsonElement parametersSchema,
        PermissionLevel level,
        OutputTrust outputTrust,
        bool writesDurableState,
        string remoteName,
        McpTaskPoller poller,
        bool metered = false,
        IServiceProvider? services = null)
    {
        _session = session;
        _remoteName = remoteName;
        _poller = poller;
        _metered = metered;
        _services = services;
        Descriptor = new ToolDescriptor(
            catalogName,
            description,
            CloneSchema(parametersSchema),
            level,
            session.ServerId,
            outputTrust,
            writesDurableState);
    }

    public ToolDescriptor Descriptor { get; }

    internal string RemoteName => _remoteName;

    public async ValueTask<ToolResult> ExecuteAsync(
        ToolArguments args, ToolExecutionContext context, CancellationToken ct)
    {
        if (!_session.IsAvailable)
        {
            return ToolResult.Failed($"MCP server '{_session.ServerId}' is not available.");
        }

        var searchTool = ToolNameProjection.Project(_session.ServerId, OpenMeteoCoordinateGate.SearchTool)
            ?? OpenMeteoCoordinateGate.SearchTool;
        if (_session.Coordinates.Reject(_remoteName, args, context.TurnId, searchTool) is { } rejected)
        {
            return ToolResult.Failed(rejected);
        }

        var client = _session.Client;
        if (client is null)
        {
            return ToolResult.Failed($"MCP server '{_session.ServerId}' is not available.");
        }

        var budget = _services?.GetService<IMeteredToolBudget>();
        var denied = await MeteredToolBudget
            .TryAdmitAsync(_metered, budget, context.UserId, context.TurnId, ct)
            .ConfigureAwait(false);
        if (denied is not null)
        {
            return ToolResult.Failed(denied);
        }

        var arguments = ToArguments(args);

        ResultOrCreatedTask<CallToolResult> outcome;
        try
        {
            outcome = await client.CallToolAsTaskAsync(
                new CallToolRequestParams
                {
                    Name = _remoteName,
                    Arguments = arguments,
                },
                ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _session.MarkUnavailable();
            return ToolResult.Failed($"MCP call to '{_remoteName}' failed: {ex.Message}");
        }

        if (outcome.IsTask)
        {
            var created = outcome.TaskCreated;
            if (created is null || string.IsNullOrWhiteSpace(created.TaskId))
            {
                return ToolResult.Failed($"MCP tool '{_remoteName}' returned a task handle without an id.");
            }

            _poller.Enqueue(client, created);
            return ToolResult.Ok($"task {created.TaskId} status=working");
        }

        if (outcome.Result is not { } result)
        {
            return ToolResult.Failed($"MCP tool '{_remoteName}' returned no result.");
        }

        var mapped = MapResult(result);
        if (mapped.Status is ToolStatus.Ok
            && string.Equals(_remoteName, OpenMeteoCoordinateGate.SearchTool, StringComparison.Ordinal)
            && mapped.Content is not null)
        {
            _session.Coordinates.Remember(context.TurnId, mapped.Content);
        }

        if (mapped.Status is ToolStatus.Ok && _metered && budget is not null)
        {
            await budget.RecordUseAsync(context.UserId, ct).ConfigureAwait(false);
        }

        return mapped;
    }

    private static Dictionary<string, JsonElement>? ToArguments(ToolArguments args)
    {
        if (string.IsNullOrWhiteSpace(args.RawJson) || args.RawJson == "{}")
        {
            return null;
        }

        using var document = JsonDocument.Parse(args.RawJson);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var arguments = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            arguments[property.Name] = property.Value.Clone();
        }

        return arguments.Count == 0 ? null : arguments;
    }

    private static ToolResult MapResult(CallToolResult result)
    {
        if (result.IsError is true)
        {
            var errorText = ExtractText(result.Content);
            return ToolResult.Failed(
                string.IsNullOrWhiteSpace(errorText)
                    ? "MCP tool returned an error."
                    : errorText);
        }

        if (result.Content is null || result.Content.Count == 0)
        {
            return ToolResult.Failed("MCP tool returned no content.");
        }

        foreach (var block in result.Content)
        {
            if (block is not TextContentBlock)
            {
                return ToolResult.Failed("MCP tool returned non-text content.");
            }
        }

        var text = ExtractText(result.Content);
        if (string.IsNullOrEmpty(text))
        {
            return ToolResult.Failed("MCP tool returned no text.");
        }

        return ToolResult.Ok(text);
    }

    private static string ExtractText(IList<ContentBlock>? content)
    {
        if (content is null || content.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var block in content)
        {
            if (block is TextContentBlock text)
            {
                builder.Append(text.Text);
            }
        }

        return builder.ToString();
    }

    private static JsonElement CloneSchema(JsonElement schema)
    {
        if (schema.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return EmptySchema.Clone();
        }

        return schema.Clone();
    }
}
