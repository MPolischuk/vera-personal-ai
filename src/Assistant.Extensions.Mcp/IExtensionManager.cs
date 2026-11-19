namespace Assistant.Extensions.Mcp;

public interface IExtensionManager
{
    public ExtensionInventory List();

    public Task<ExtensionMutationResult> SetEnabledAsync(
        string serverId,
        bool enabled,
        CancellationToken cancellationToken);

    public Task<ExtensionMutationResult> ClassifyAsync(
        string serverId,
        string toolName,
        string permissionLevel,
        string outputTrust,
        bool writesDurableState,
        CancellationToken cancellationToken);
}

public enum ExtensionMutationResult
{
    Success,
    NotFound,
    Invalid,
}

public sealed record ExtensionInventory(IReadOnlyList<ExtensionServerSnapshot> Servers);

public sealed record ExtensionServerSnapshot(
    string Id,
    bool Enabled,
    string Transport,
    string Status,
    IReadOnlyList<ExtensionToolSnapshot> Tools);

public sealed record ExtensionToolSnapshot(
    string RemoteName,
    string? CatalogName,
    string PermissionLevel,
    string OutputTrust,
    bool WritesDurableState,
    bool Declared);
