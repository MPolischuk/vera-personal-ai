using System.Text;
using System.Text.RegularExpressions;

namespace Assistant.Extensions.Mcp;

internal static partial class ToolNameProjection
{
    [GeneratedRegex("^[a-z][a-z0-9_]{0,19}$", RegexOptions.CultureInvariant)]
    private static partial Regex ServerIdPattern();

    public static bool IsValidServerId(string serverId)
        => !string.IsNullOrEmpty(serverId) && ServerIdPattern().IsMatch(serverId);

    /// <summary>
    /// Builds <c>{serverId}_{sanitized}</c>, or null when the MCP name cannot be projected.
    /// </summary>
    public static string? Project(string serverId, string mcpToolName)
    {
        var sanitized = Sanitize(mcpToolName);
        if (sanitized.Length == 0)
        {
            return null;
        }

        var projected = $"{serverId}_{sanitized}";
        return projected.Length <= 64 ? projected : null;
    }

    private static string Sanitize(string mcpToolName)
    {
        if (string.IsNullOrEmpty(mcpToolName))
        {
            return string.Empty;
        }

        var lower = mcpToolName.ToLowerInvariant();
        var buffer = new StringBuilder(lower.Length);
        var previousUnderscore = false;

        foreach (var ch in lower)
        {
            if (ch is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_')
            {
                if (ch == '_')
                {
                    if (previousUnderscore || buffer.Length == 0)
                    {
                        continue;
                    }

                    buffer.Append('_');
                    previousUnderscore = true;
                }
                else
                {
                    buffer.Append(ch);
                    previousUnderscore = false;
                }
            }
            else
            {
                if (buffer.Length == 0 || previousUnderscore)
                {
                    continue;
                }

                buffer.Append('_');
                previousUnderscore = true;
            }
        }

        if (buffer.Length > 0 && buffer[^1] == '_')
        {
            buffer.Length--;
        }

        return buffer.ToString();
    }
}
