using Assistant.Abstractions.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Assistant.Extensions.Mcp;

internal sealed class ExtensionManager(
    IOptions<McpOptions> options,
    McpSessionHost host,
    ILogger<ExtensionManager> logger) : IExtensionManager, IDisposable
{
    private static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        .Build();

    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    public ExtensionInventory List()
    {
        var servers = options.Value.Servers
            .Select(BuildServerSnapshot)
            .ToArray();
        return new ExtensionInventory(servers);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _gate.Dispose();
    }

    public async Task<ExtensionMutationResult> SetEnabledAsync(
        string serverId,
        bool enabled,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var server = FindServer(serverId);
            if (server is null)
            {
                return ExtensionMutationResult.NotFound;
            }

            server.Enabled = enabled;
            WriteManifest();
            McpLog.ServerApplying(logger, serverId);
            await host.ApplyServerAsync(serverId, cancellationToken);
            return ExtensionMutationResult.Success;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ExtensionMutationResult> ClassifyAsync(
        string serverId,
        string toolName,
        string permissionLevel,
        string outputTrust,
        bool writesDurableState,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            return ExtensionMutationResult.Invalid;
        }

        if (!TryParseExecutableLevel(permissionLevel, out var level)
            || string.IsNullOrWhiteSpace(outputTrust)
            || !TryParseOutputTrust(outputTrust, out var trust))
        {
            return ExtensionMutationResult.Invalid;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var server = FindServer(serverId);
            if (server is null)
            {
                return ExtensionMutationResult.NotFound;
            }

            if (!IsAcceptedToolName(server, toolName))
            {
                return ExtensionMutationResult.NotFound;
            }

            server.Tools.TryGetValue(toolName, out var previous);
            server.Tools[toolName] = new McpToolManifestOptions
            {
                PermissionLevel = level.ToString(),
                OutputTrust = trust.ToString(),
                WritesDurableState = writesDurableState,
                Metered = previous?.Metered ?? false,
            };

            WriteManifest();
            McpLog.ServerApplying(logger, serverId);
            await host.ApplyServerAsync(serverId, cancellationToken);
            return ExtensionMutationResult.Success;
        }
        finally
        {
            _gate.Release();
        }
    }

    private ExtensionServerSnapshot BuildServerSnapshot(McpServerOptions server)
    {
        var status = ResolveStatus(server);
        var tools = BuildToolSnapshots(server);
        return new ExtensionServerSnapshot(
            server.Id,
            server.Enabled,
            server.Transport,
            status,
            tools);
    }

    private string ResolveStatus(McpServerOptions server)
    {
        if (!server.Enabled)
        {
            return "Disabled";
        }

        if (host.TryGetSessionStatus(server.Id, out var status))
        {
            return status;
        }

        return "NotConnected";
    }

    private ExtensionToolSnapshot[] BuildToolSnapshots(McpServerOptions server)
    {
        var byRemote = new Dictionary<string, ExtensionToolSnapshot>(StringComparer.Ordinal);

        if (host.TryGetSessionTools(server.Id, out var sessionTools))
        {
            foreach (var tool in sessionTools)
            {
                if (tool is not ProjectedMcpTool projected)
                {
                    continue;
                }

                byRemote[projected.RemoteName] = ToSnapshot(
                    projected.RemoteName,
                    projected.Descriptor.Name,
                    server);
            }
        }

        foreach (var key in server.Tools.Keys)
        {
            if (byRemote.ContainsKey(key))
            {
                continue;
            }

            byRemote[key] = ToSnapshot(key, catalogName: null, server);
        }

        return byRemote.Values.OrderBy(t => t.RemoteName, StringComparer.Ordinal).ToArray();
    }

    private static ExtensionToolSnapshot ToSnapshot(
        string remoteName,
        string? catalogName,
        McpServerOptions server)
    {
        var declared = TryGetDeclaredPolicy(server, remoteName, out var level, out var trust, out var writes);
        if (!declared)
        {
            return new ExtensionToolSnapshot(
                remoteName,
                catalogName,
                PermissionLevel.Undeclared.ToString(),
                OutputTrust.Untrusted.ToString(),
                false,
                Declared: false);
        }

        return new ExtensionToolSnapshot(
            remoteName,
            catalogName,
            level.ToString(),
            trust.ToString(),
            writes,
            Declared: true);
    }

    private bool IsAcceptedToolName(McpServerOptions server, string toolName)
    {
        if (host.TryGetSessionTools(server.Id, out var sessionTools)
            && sessionTools.OfType<ProjectedMcpTool>().Any(t =>
                string.Equals(t.RemoteName, toolName, StringComparison.Ordinal)))
        {
            return true;
        }

        return server.Tools.ContainsKey(toolName);
    }

    private McpServerOptions? FindServer(string serverId)
        => options.Value.Servers
            .FirstOrDefault(s => string.Equals(s.Id, serverId, StringComparison.Ordinal));

    private void WriteManifest()
    {
        var path = ExtensionManifestLoader.Resolve(options.Value.ManifestPath);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var document = new ExtensionManifestDocument { Servers = options.Value.Servers };
        var yaml = Serializer.Serialize(document);
        File.WriteAllText(path, yaml);
    }

    private static bool TryGetDeclaredPolicy(
        McpServerOptions server,
        string remoteName,
        out PermissionLevel level,
        out OutputTrust trust,
        out bool writesDurableState)
    {
        level = PermissionLevel.Undeclared;
        trust = OutputTrust.Untrusted;
        writesDurableState = false;

        if (!server.Tools.TryGetValue(remoteName, out var entry) || entry is null)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(entry.PermissionLevel)
            || !TryParseExecutableLevel(entry.PermissionLevel, out level))
        {
            return false;
        }

        if (!TryParseOutputTrust(entry.OutputTrust, out trust))
        {
            trust = OutputTrust.Untrusted;
        }

        writesDurableState = entry.WritesDurableState;
        return true;
    }

    private static bool TryParseExecutableLevel(string? text, out PermissionLevel level)
    {
        level = default;
        if (string.IsNullOrWhiteSpace(text)
            || !Enum.TryParse(text, ignoreCase: true, out level)
            || !Enum.IsDefined(level)
            || level is PermissionLevel.Undeclared)
        {
            level = default;
            return false;
        }

        return true;
    }

    private static bool TryParseOutputTrust(string? text, out OutputTrust trust)
    {
        trust = OutputTrust.Untrusted;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        return Enum.TryParse(text, ignoreCase: true, out trust) && Enum.IsDefined(trust);
    }

    private sealed class ExtensionManifestDocument
    {
        public List<McpServerOptions> Servers { get; set; } = [];
    }
}
