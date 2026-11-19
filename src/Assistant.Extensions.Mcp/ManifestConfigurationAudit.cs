using Assistant.Abstractions.Tools;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Assistant.Extensions.Mcp;

public static class ManifestConfigurationAudit
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .Build();

    public static ManifestInspection Inspect(
        string yamlText,
        IReadOnlyDictionary<string, string>? servicePins = null)
    {
        ArgumentNullException.ThrowIfNull(yamlText);

        if (string.IsNullOrWhiteSpace(yamlText))
        {
            return Empty();
        }

        ExtensionManifestDocument? document;
        try
        {
            document = Deserializer.Deserialize<ExtensionManifestDocument>(yamlText);
        }
        catch (Exception)
        {
            return Invalid();
        }

        if (document is null || document.Servers is null)
        {
            return Empty();
        }

        var findings = new List<ManifestAuditFinding>();
        var usable = new HashSet<string>(StringComparer.Ordinal);
        var enabled = new HashSet<string>(StringComparer.Ordinal);
        var pinned = new List<PinnedMcpServer>();

        foreach (var server in document.Servers)
        {
            if (server is null)
            {
                continue;
            }

            CollectSecrets(server, findings);
            if (!server.Enabled)
            {
                continue;
            }

            var id = server.Id ?? string.Empty;
            enabled.Add(id);
            CollectTools(server, findings, usable);
            if (TryAcceptVersion(server, servicePins, out var version))
            {
                pinned.Add(new PinnedMcpServer(id, version));
            }
            else
            {
                findings.Add(new ManifestAuditFinding("error", "mcp.version_unpinned", id));
            }
        }

        return new ManifestInspection(findings, usable, enabled, pinned);
    }

    private static bool TryAcceptVersion(
        McpServerOptions server,
        IReadOnlyDictionary<string, string>? servicePins,
        out string version)
    {
        version = server.Version?.Trim() ?? string.Empty;
        if (version.Length == 0 || string.Equals(version, "latest", StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.Equals(version, "build", StringComparison.Ordinal))
        {
            return true;
        }

        // Compose service name is the server id plus the "-mcp" suffix.
        var serviceName = (server.Id ?? string.Empty) + "-mcp";
        return servicePins is not null
            && servicePins.TryGetValue(serviceName, out var pin)
            && string.Equals(pin, "build", StringComparison.Ordinal);
    }

    private static void CollectSecrets(McpServerOptions server, List<ManifestAuditFinding> findings)
    {
        var id = server.Id ?? string.Empty;
        if (SecretReferences.ContainsPlaceholder(server.Endpoint))
        {
            findings.Add(Secret($"servers[{id}].endpoint"));
        }

        if (SecretReferences.ContainsPlaceholder(server.Command))
        {
            findings.Add(Secret($"servers[{id}].command"));
        }

        if (server.Arguments is not null)
        {
            for (var index = 0; index < server.Arguments.Count; index++)
            {
                if (SecretReferences.ContainsPlaceholder(server.Arguments[index]))
                {
                    findings.Add(Secret($"servers[{id}].arguments[{index}]"));
                }
            }
        }

        if (server.Headers is not null)
        {
            foreach (var (name, raw) in server.Headers)
            {
                if (!SecretReferences.IsAcceptableHeaderReference(raw))
                {
                    findings.Add(Secret($"servers[{id}].headers.{name}"));
                }
            }
        }

        if (server.Environment is not null)
        {
            foreach (var (name, raw) in server.Environment)
            {
                if (!SecretReferences.IsAcceptableEnvironmentReference(raw))
                {
                    findings.Add(Secret($"servers[{id}].environment.{name}"));
                }
            }
        }
    }

    private static void CollectTools(
        McpServerOptions server,
        List<ManifestAuditFinding> findings,
        HashSet<string> usable)
    {
        if (server.Tools is null)
        {
            return;
        }

        var id = server.Id ?? string.Empty;
        foreach (var (remoteName, tool) in server.Tools)
        {
            if (!IsUsable(tool))
            {
                findings.Add(new ManifestAuditFinding(
                    "warning",
                    "tool.unclassified",
                    $"servers[{id}].tools.{remoteName}"));
                continue;
            }

            var projected = ToolNameProjection.Project(id, remoteName);
            if (projected is not null)
            {
                usable.Add(projected);
            }
        }
    }

    private static bool IsUsable(McpToolManifestOptions? tool)
        => tool is not null
           && IsUsableLevel(tool.PermissionLevel)
           && IsReadableTrust(tool.OutputTrust);

    private static bool IsUsableLevel(string? text)
        => TryParseToken(text, out PermissionLevel level)
           && level is PermissionLevel.Read or PermissionLevel.ReversibleAction or PermissionLevel.SensitiveAction;

    private static bool IsReadableTrust(string? text)
        => TryParseToken(text, out OutputTrust _);

    private static bool TryParseToken<TEnum>(string? text, out TEnum value)
        where TEnum : struct, Enum
    {
        value = default;
        if (string.IsNullOrWhiteSpace(text) || !char.IsAsciiLetter(text[0]))
        {
            return false;
        }

        return Enum.TryParse(text, ignoreCase: true, out value) && Enum.IsDefined(value);
    }

    private static ManifestAuditFinding Secret(string subject)
        => new("error", "manifest.literal_secret", subject);

    private static ManifestInspection Invalid()
        => new(
            [new ManifestAuditFinding("error", "manifest.invalid", "extensions.yaml")],
            new HashSet<string>(StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal),
            []);

    private static ManifestInspection Empty()
        => new(
            [],
            new HashSet<string>(StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal),
            []);

    private sealed class ExtensionManifestDocument
    {
        public ExtensionManifestDocument()
        {
        }

        public List<McpServerOptions>? Servers { get; set; }
    }
}

public sealed record ManifestInspection(
    IReadOnlyList<ManifestAuditFinding> Findings,
    IReadOnlySet<string> UsableToolNames,
    IReadOnlySet<string> EnabledServerIds,
    IReadOnlyList<PinnedMcpServer> PinnedServers);

public readonly record struct PinnedMcpServer(string Id, string Version);

public readonly record struct ManifestAuditFinding(string Severity, string Code, string Subject);
