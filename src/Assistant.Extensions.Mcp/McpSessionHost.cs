using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Assistant.Abstractions.Skills;
using Assistant.Abstractions.Tools;
using Assistant.Abstractions.Proactive;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Extensions.Tasks;
using ModelContextProtocol.Protocol;

namespace Assistant.Extensions.Mcp;

internal sealed class McpSessionHost(
    IOptions<McpOptions> options,
    IMcpToolClassification classification,
    McpTaskPoller poller,
    ILogger<McpSessionHost> logger,
    IServiceProvider? services = null) : IHostedService, IToolSource, IExternalSkillSource, IConnectedMcpServers
{
    private static readonly Regex IsValidPromptName = new(
        "^[A-Za-z0-9._:-]{1,64}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly ConcurrentDictionary<string, ServerSession> _sessions = new(StringComparer.Ordinal);

    public IReadOnlyList<ITool> Tools
        => _sessions.Values
            .Where(s => s.IsAvailable)
            .SelectMany(s => s.Tools)
            .ToArray();

    public bool IsConnected(string serverId)
        => _sessions.TryGetValue(serverId, out var session) && session.IsAvailable;

    public IReadOnlyList<ExternalSkillSummary> List()
    {
        var prompts = new List<ExternalSkillSummary>();
        foreach (var session in _sessions.Values)
        {
            if (!session.IsAvailable)
            {
                continue;
            }

            foreach (var prompt in session.Prompts)
            {
                prompts.Add(new ExternalSkillSummary(
                    $"{session.ServerId}/{prompt.Name}",
                    prompt.Description,
                    session.ServerId));
            }
        }

        return prompts;
    }

    public async ValueTask<string?> LoadTextAsync(string key, CancellationToken ct)
    {
        if (!TryParsePromptKey(key, out var serverId, out var promptName)
            || !_sessions.TryGetValue(serverId, out var session)
            || !session.IsAvailable
            || session.Client is null)
        {
            return null;
        }

        var result = await session.Client.GetPromptAsync(promptName, cancellationToken: ct);

        var parts = new List<string>();
        if (result.Messages is not null)
        {
            foreach (var message in result.Messages)
            {
                if (message.Content is TextContentBlock text && !string.IsNullOrWhiteSpace(text.Text))
                {
                    parts.Add(text.Text);
                }
            }
        }

        return parts.Count == 0 ? null : string.Join("\n\n", parts);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // A present manifest replaces Servers. A missing file keeps the in-memory list.
        ExtensionManifestLoader.TryApply(options.Value, logger);

        foreach (var server in options.Value.Servers)
        {
            await ConnectServerAsync(server, cancellationToken);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var session in _sessions.Values)
        {
            await session.DisposeAsync();
        }

        _sessions.Clear();
    }

    /// <summary>
    /// Disposes the session for <paramref name="serverId"/> if present and, when the
    /// server remains enabled, reconnects it from the current options.
    /// </summary>
    internal async Task ApplyServerAsync(string serverId, CancellationToken cancellationToken)
    {
        if (_sessions.TryRemove(serverId, out var existing))
        {
            await existing.DisposeAsync();
        }

        var server = options.Value.Servers
            .FirstOrDefault(s => string.Equals(s.Id, serverId, StringComparison.Ordinal));
        if (server is null)
        {
            return;
        }

        await ConnectServerAsync(server, cancellationToken);
    }

    /// <summary>
    /// Reports the live session status without exposing <see cref="ServerSession"/>.
    /// Returns false when there is no session for <paramref name="serverId"/>.
    /// </summary>
    internal bool TryGetSessionStatus(string serverId, out string status)
    {
        if (_sessions.TryGetValue(serverId, out var session))
        {
            status = session.IsAvailable ? "Connected" : "Unavailable";
            return true;
        }

        status = "NotConnected";
        return false;
    }

    internal bool TryGetSessionTools(string serverId, out IReadOnlyList<ITool> tools)
    {
        if (_sessions.TryGetValue(serverId, out var session) && session.IsAvailable)
        {
            tools = session.Tools;
            return true;
        }

        tools = [];
        return false;
    }

    internal bool TryGetClient(string serverId, [NotNullWhen(true)] out McpClient? client)
    {
        if (_sessions.TryGetValue(serverId, out var session)
            && session.IsAvailable
            && session.Client is not null)
        {
            client = session.Client;
            return true;
        }

        client = null;
        return false;
    }

    private async Task ConnectServerAsync(McpServerOptions server, CancellationToken cancellationToken)
    {
        if (!ToolNameProjection.IsValidServerId(server.Id))
        {
            McpLog.InvalidServerId(logger, server.Id);
            return;
        }

        if (!server.Enabled)
        {
            McpLog.ServerDisabled(logger, server.Id);
            return;
        }

        if (!SecretReferences.TryResolve(
                server,
                Environment.GetEnvironmentVariable,
                out var headers,
                out var environment,
                out var detail))
        {
            McpLog.SecretRejected(logger, server.Id, detail);
            return;
        }

        if (!await TryApplyOAuthAsync(server, headers, cancellationToken))
        {
            return;
        }

        IClientTransport? transport = null;
        try
        {
            transport = CreateTransport(server, headers, environment);
            if (transport is null)
            {
                McpLog.MissingTransportFields(logger, server.Id, server.Transport);
                return;
            }

            var client = await McpClient.CreateAsync(
                transport,
                clientOptions: CreateClientOptions(),
                cancellationToken: cancellationToken);
            var listed = await client.ListToolsAsync(cancellationToken: cancellationToken);
            var session = new ServerSession(server.Id, client, transport);
            transport = null;

            foreach (var mcpTool in listed)
            {
                if (CalendarWriteTools.ShouldOmit(server.Id, mcpTool.Name))
                {
                    McpLog.OmittingCalendarWrite(logger, mcpTool.Name, server.Id);
                    continue;
                }

                var catalogName = ToolNameProjection.Project(server.Id, mcpTool.Name);
                if (catalogName is null)
                {
                    McpLog.OmittingTool(logger, mcpTool.Name, server.Id);
                    continue;
                }

                var policy = classification.PolicyFor(server.Id, mcpTool.Name);
                var projected = new ProjectedMcpTool(
                    session,
                    catalogName,
                    OpenMeteoCoordinateGate.Describe(mcpTool.Name, mcpTool.Description ?? string.Empty),
                    mcpTool.JsonSchema,
                    policy.Level,
                    policy.OutputTrust,
                    policy.WritesDurableState,
                    mcpTool.Name,
                    poller,
                    IsMetered(options.Value, server.Id, mcpTool.Name),
                    services);
                session.AddTool(projected);
            }

            try
            {
                var prompts = await client.ListPromptsAsync(cancellationToken: cancellationToken);
                foreach (var prompt in prompts)
                {
                    if (string.IsNullOrEmpty(prompt.Name) || !IsValidPromptName.IsMatch(prompt.Name))
                    {
                        McpLog.OmittingPrompt(logger, prompt.Name ?? string.Empty, server.Id);
                        continue;
                    }

                    if (!session.TryAddPrompt(prompt.Name, prompt.Description ?? string.Empty))
                    {
                        McpLog.OmittingPrompt(logger, prompt.Name, server.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                // A server without the prompts capability must not drop tools already stored.
                McpLog.PromptListFailed(logger, ex, server.Id);
            }

            _sessions[server.Id] = session;
            McpLog.ServerConnected(logger, server.Id, session.Tools.Count);
        }
        catch (Exception ex)
        {
            McpLog.ServerConnectFailed(logger, ex, server.Id);
            if (transport is not null)
            {
                await DisposeTransportAsync(transport);
            }
        }
    }

    private async Task<bool> TryApplyOAuthAsync(
        McpServerOptions server,
        Dictionary<string, string> headers,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(server.Auth, "oauth", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        headers.Remove("Authorization");
        var refresher = services?.GetService<IMcpAccessTokenRefresher>();
        var access = refresher is null
            ? null
            : await refresher.GetAccessTokenAsync(server.Id, cancellationToken);
        if (string.IsNullOrEmpty(access))
        {
            McpLog.OAuthDisconnected(logger, server.Id);
            return false;
        }

        headers["Authorization"] = "Bearer " + access;
        return true;
    }

    private static bool IsMetered(McpOptions mcp, string serverId, string toolName)
    {
        foreach (var server in mcp.Servers)
        {
            if (!string.Equals(server.Id, serverId, StringComparison.Ordinal))
            {
                continue;
            }

            return server.Tools is not null
                && server.Tools.TryGetValue(toolName, out var tool)
                && tool.Metered;
        }

        return false;
    }

    private static IClientTransport? CreateTransport(
        McpServerOptions server,
        Dictionary<string, string> headers,
        Dictionary<string, string?> environment)
    {
        if (string.Equals(server.Transport, "Stdio", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(server.Command))
            {
                return null;
            }

            var stdio = new StdioClientTransportOptions
            {
                Name = server.Id,
                Command = server.Command,
                Arguments = server.Arguments,
            };

            if (!string.IsNullOrWhiteSpace(server.WorkingDirectory))
            {
                stdio.WorkingDirectory = server.WorkingDirectory;
            }

            if (environment.Count > 0)
            {
                stdio.EnvironmentVariables = new Dictionary<string, string?>(environment, StringComparer.Ordinal);
            }

            return new StdioClientTransport(stdio);
        }

        if (string.Equals(server.Transport, "StreamableHttp", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(server.Endpoint)
                || !Uri.TryCreate(server.Endpoint, UriKind.Absolute, out var endpoint))
            {
                return null;
            }

            var http = new HttpClientTransportOptions
            {
                Name = server.Id,
                Endpoint = endpoint,
                TransportMode = HttpTransportMode.StreamableHttp,
            };

            if (headers.Count > 0)
            {
                http.AdditionalHeaders = new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase);
            }

            return new HttpClientTransport(http);
        }

        return null;
    }

    private static McpClientOptions CreateClientOptions() => new()
    {
        Capabilities = new ClientCapabilities
        {
            Extensions = new Dictionary<string, object>
            {
                [TasksProtocol.ExtensionId] = new JsonObject(),
            },
        },
    };

    private static bool TryParsePromptKey(string key, out string serverId, out string promptName)
    {
        serverId = string.Empty;
        promptName = string.Empty;

        var slash = key.IndexOf('/', StringComparison.Ordinal);
        if (slash <= 0 || slash != key.LastIndexOf('/'))
        {
            return false;
        }

        serverId = key[..slash];
        promptName = key[(slash + 1)..];
        return IsValidPromptName.IsMatch(promptName);
    }

    private static async Task DisposeTransportAsync(IClientTransport transport)
    {
        if (transport is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
        }
        else if (transport is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    internal sealed class ServerSession : IAsyncDisposable
    {
        private readonly List<ITool> _tools = [];
        private readonly IClientTransport _transport;
        private int _unavailable;

        public ServerSession(string serverId, McpClient client, IClientTransport transport)
        {
            ServerId = serverId;
            Client = client;
            _transport = transport;
        }

        public string ServerId { get; }

        public OpenMeteoCoordinateGate Coordinates { get; } = new();

        public McpClient? Client { get; private set; }

        public bool IsAvailable => Volatile.Read(ref _unavailable) == 0;

        public IReadOnlyList<ITool> Tools => _tools;

        public void AddTool(ITool tool) => _tools.Add(tool);

        public bool TryAddPrompt(string name, string description)
        {
            if (_prompts.Exists(prompt => string.Equals(prompt.Name, name, StringComparison.Ordinal)))
            {
                return false;
            }

            _prompts.Add(new StoredPrompt(name, description));
            return true;
        }

        public IReadOnlyList<StoredPrompt> Prompts => _prompts;

        public readonly record struct StoredPrompt(string Name, string Description);

        private readonly List<StoredPrompt> _prompts = [];

        public void MarkUnavailable()
        {
            if (Interlocked.Exchange(ref _unavailable, 1) == 0)
            {
                Client = null;
            }
        }

        public async ValueTask DisposeAsync()
        {
            MarkUnavailable();

            if (Client is IAsyncDisposable clientDisposable)
            {
                await clientDisposable.DisposeAsync();
            }

            await DisposeTransportAsync(_transport);
        }
    }
}
