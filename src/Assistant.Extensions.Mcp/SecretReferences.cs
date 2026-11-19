using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Assistant.Extensions.Mcp;

internal static partial class SecretReferences
{
    [GeneratedRegex(@"^\$\{([A-Za-z_][A-Za-z0-9_]*)\}$", RegexOptions.CultureInvariant)]
    private static partial Regex ExactReference();

    [GeneratedRegex(@"^Bearer \$\{([A-Za-z_][A-Za-z0-9_]*)\}$", RegexOptions.CultureInvariant)]
    private static partial Regex BearerReference();

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex EnvironmentName();

    public static bool TryResolve(
        McpServerOptions server,
        Func<string, string?> resolveVariable,
        out Dictionary<string, string> headers,
        out Dictionary<string, string?> environment,
        [NotNullWhen(false)] out string? detail)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(resolveVariable);

        headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        environment = new Dictionary<string, string?>(StringComparer.Ordinal);

        if (ContainsPlaceholder(server.Endpoint))
        {
            return Fail(headers, environment, "endpoint contains '${'", out detail);
        }

        if (ContainsPlaceholder(server.Command))
        {
            return Fail(headers, environment, "command contains '${'", out detail);
        }

        if (server.Arguments is not null)
        {
            foreach (var argument in server.Arguments)
            {
                if (ContainsPlaceholder(argument))
                {
                    return Fail(headers, environment, "argument contains '${'", out detail);
                }
            }
        }

        if (server.Headers is not null)
        {
            foreach (var (name, raw) in server.Headers)
            {
                if (!TryParseHeader(raw, out var variable, out var bearer))
                {
                    return Fail(headers, environment, $"header '{name}' is not a ${{NAME}} reference", out detail);
                }

                if (!TryReadVariable(resolveVariable, variable, out var value, out detail))
                {
                    headers.Clear();
                    environment.Clear();
                    return false;
                }

                headers[name] = bearer ? "Bearer " + value : value;
            }
        }

        if (server.Environment is not null)
        {
            foreach (var (name, raw) in server.Environment)
            {
                if (string.IsNullOrEmpty(name) || !EnvironmentName().IsMatch(name))
                {
                    return Fail(headers, environment, $"environment name '{name}' is invalid", out detail);
                }

                var match = ExactReference().Match(raw ?? string.Empty);
                if (!match.Success)
                {
                    return Fail(headers, environment, $"environment '{name}' is not a ${{NAME}} reference", out detail);
                }

                if (!TryReadVariable(resolveVariable, match.Groups[1].Value, out var value, out detail))
                {
                    headers.Clear();
                    environment.Clear();
                    return false;
                }

                environment[name] = value;
            }
        }

        detail = null;
        return true;
    }

    private static bool Fail(
        Dictionary<string, string> headers,
        Dictionary<string, string?> environment,
        string reason,
        out string detail)
    {
        headers.Clear();
        environment.Clear();
        detail = reason;
        return false;
    }

    private static bool TryParseHeader(string? raw, out string variable, out bool bearer)
    {
        variable = string.Empty;
        bearer = false;
        if (string.IsNullOrEmpty(raw))
        {
            return false;
        }

        var bearerMatch = BearerReference().Match(raw);
        if (bearerMatch.Success)
        {
            bearer = true;
            variable = bearerMatch.Groups[1].Value;
            return true;
        }

        var exact = ExactReference().Match(raw);
        if (!exact.Success)
        {
            return false;
        }

        variable = exact.Groups[1].Value;
        return true;
    }

    private static bool TryReadVariable(
        Func<string, string?> resolveVariable,
        string variable,
        [NotNullWhen(true)] out string? value,
        [NotNullWhen(false)] out string? detail)
    {
        value = resolveVariable(variable);
        if (string.IsNullOrWhiteSpace(value))
        {
            detail = $"environment variable '{variable}' is missing or empty";
            return false;
        }

        detail = null;
        return true;
    }

    internal static bool IsAcceptableHeaderReference(string? raw)
        => TryParseHeader(raw, out _, out _);

    internal static bool IsAcceptableEnvironmentReference(string? raw)
        => ExactReference().IsMatch(raw ?? string.Empty);

    internal static bool ContainsPlaceholder(string? value)
        => value?.Contains("${", StringComparison.Ordinal) == true;
}
