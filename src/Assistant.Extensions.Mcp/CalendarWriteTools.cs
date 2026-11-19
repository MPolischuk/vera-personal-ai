namespace Assistant.Extensions.Mcp;

/// <summary>
/// Calendar write tools the catalog must not expose, even when the manifest lists them.
/// </summary>
public static class CalendarWriteTools
{
    public const string ServerId = "calendar";

    public static readonly string[] Names =
    [
        "create_event",
        "update_event",
        "delete_event",
        "respond_to_event",
    ];

    public static bool ShouldOmit(string serverId, string? toolName)
    {
        if (!string.Equals(serverId, ServerId, StringComparison.Ordinal) || string.IsNullOrEmpty(toolName))
        {
            return false;
        }

        var normalized = toolName.Replace('-', '_').ToLowerInvariant();
        foreach (var name in Names)
        {
            if (string.Equals(name, normalized, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
