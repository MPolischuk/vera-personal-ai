namespace Assistant.Extensions.Mcp;

public sealed class McpOptions
{
    public const string SectionName = "Mcp";

    /// <summary>
    /// Path to the extension manifest. Relative paths resolve against <see cref="AppContext.BaseDirectory"/>.
    /// </summary>
    public string ManifestPath { get; set; } = "config/extensions.yaml";

    public List<McpServerOptions> Servers { get; set; } = [];
}

public sealed class McpServerOptions
{
    /// <summary>Stable id used as ExtensionName and name prefix. Max 20 chars.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>When false, the host does not connect and the server contributes no tools.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Pinned release of an enabled server. <c>build</c> when compose builds the service and publishes no image.
    /// Otherwise a tag or digest. <c>latest</c> is not a pin.
    /// </summary>
    public string? Version { get; set; }

    /// <summary><c>Stdio</c> or <c>StreamableHttp</c>.</summary>
    public string Transport { get; set; } = string.Empty;

    /// <summary><c>none</c> or <c>oauth</c>. OAuth does not use a static bearer.</summary>
    public string Auth { get; set; } = "none";

    public string? Command { get; set; }

    public List<string> Arguments { get; set; } = [];

    public string? Endpoint { get; set; }

    public string? WorkingDirectory { get; set; }

    public Dictionary<string, string> Headers { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> Environment { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Policies keyed by the remote MCP tool name, not the catalog name.</summary>
    public Dictionary<string, McpToolManifestOptions> Tools { get; set; } = new(StringComparer.Ordinal);
}

public sealed class McpToolManifestOptions
{
    public string? PermissionLevel { get; set; }

    public string? OutputTrust { get; set; }

    public bool WritesDurableState { get; set; }

    /// <summary>
    /// When true, the projected tool consumes the metered budget before the call.
    /// Without a registered budget the tool fails closed and does not call the server.
    /// </summary>
    public bool Metered { get; set; }
}
