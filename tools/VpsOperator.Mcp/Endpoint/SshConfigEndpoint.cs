using System.Diagnostics;
using System.Text.RegularExpressions;

namespace VpsOperator.Mcp;

internal interface IVpsEndpoint
{
    public Task<string?> ResolveHostAsync(CancellationToken cancellationToken);
}

internal sealed partial class SshConfigEndpoint : IVpsEndpoint
{
    public async Task<string?> ResolveHostAsync(CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo
        {
            FileName = "ssh",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add("-G");
        start.ArgumentList.Add(SshRemoteShell.HostAlias);

        using var process = new Process { StartInfo = start };
        try
        {
            if (!process.Start())
            {
                return null;
            }
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }

        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            return null;
        }

        string? host = null;
        foreach (var line in output.Split('\n'))
        {
            var normalized = line.EndsWith('\r') ? line[..^1] : line;
            if (normalized.StartsWith("hostname ", StringComparison.Ordinal))
            {
                host = normalized["hostname ".Length..].Trim();
            }
        }

        return host is not null && Host().IsMatch(host) ? host : null;
    }

    [GeneratedRegex(@"^[A-Za-z0-9.-]{1,253}$", RegexOptions.CultureInvariant)]
    private static partial Regex Host();
}
