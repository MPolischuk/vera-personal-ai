using Microsoft.Extensions.Logging;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Assistant.Extensions.Mcp;

internal static class ExtensionManifestLoader
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        // Without IgnoreUnmatchedProperties: an unknown key is an invalid file.
        .Build();

    /// <summary>
    /// Applies the manifest when the file exists.
    /// Returns false when the file is missing and leaves <paramref name="options"/> unchanged.
    /// Returns true when the file was applied, including when it contributed zero servers.
    /// </summary>
    public static bool TryApply(McpOptions options, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        var path = Resolve(options.ManifestPath);
        if (!File.Exists(path))
        {
            return false;
        }

        ExtensionManifestDocument? document;
        try
        {
            var text = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(text))
            {
                options.Servers = [];
                McpLog.ManifestEmpty(logger, path);
                return true;
            }

            document = Deserializer.Deserialize<ExtensionManifestDocument>(text);
        }
        catch (Exception)
        {
            options.Servers = [];
            McpLog.ManifestInvalid(logger, path);
            return true;
        }

        if (document is null || document.Servers is null)
        {
            options.Servers = [];
            McpLog.ManifestEmpty(logger, path);
            return true;
        }

        if (HasDuplicateId(document.Servers))
        {
            options.Servers = [];
            McpLog.ManifestDuplicateServerId(logger, path);
            return true;
        }

        options.Servers = document.Servers;
        return true;
    }

    private static bool HasDuplicateId(List<McpServerOptions> servers)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var server in servers)
        {
            if (server is null || !seen.Add(server.Id ?? string.Empty))
            {
                return true;
            }
        }

        return false;
    }

    internal static string Resolve(string path)
        => Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);

    private sealed class ExtensionManifestDocument
    {
        // Public so YamlDotNet can construct the nested type. The implicit constructor would be private.
        public ExtensionManifestDocument()
        {
        }

        public List<McpServerOptions>? Servers { get; set; }
    }
}
