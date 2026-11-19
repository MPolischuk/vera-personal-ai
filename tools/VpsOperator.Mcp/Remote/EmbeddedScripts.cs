using System.Reflection;
using System.Text;

namespace VpsOperator.Mcp;

internal static class EmbeddedScripts
{
    public static string Load(string scriptName)
    {
        if (!scriptName.EndsWith(".sh", StringComparison.Ordinal) || scriptName.Contains('/', StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The script name is not a local bash file.");
        }

        var assembly = typeof(EmbeddedScripts).Assembly;
        var resource = assembly.GetManifestResourceNames()
            .SingleOrDefault(name => name.EndsWith("." + scriptName, StringComparison.Ordinal));
        if (resource is null)
        {
            throw new InvalidOperationException("The remote script is not embedded: " + scriptName);
        }

        using var stream = assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException("The remote script could not be read.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var body = reader.ReadToEnd();
        if (body.Contains('\r', StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The remote script contains CR: " + scriptName);
        }

        return RemotePrelude.Text + "\n" + body;
    }

    public static IReadOnlyList<string> Names() =>
        typeof(EmbeddedScripts).Assembly.GetManifestResourceNames()
            .Where(name => name.EndsWith(".sh", StringComparison.Ordinal))
            .Select(name =>
            {
                var parts = name.Split('.');
                return parts[^2] + ".sh";
            })
            .ToArray();
}
