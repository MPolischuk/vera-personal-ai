using System.Diagnostics;
using System.Text;

namespace VpsOperator.Mcp;

internal sealed class SshRemoteShell : IRemoteShell
{
    /// <summary>
    /// SSH config alias from <c>ASSISTANT_SSH_HOST</c>. The repository does not store an address.
    /// </summary>
    public static string HostAlias =>
        Environment.GetEnvironmentVariable("ASSISTANT_SSH_HOST") is { Length: > 0 } host
            ? host.Trim()
            : throw new InvalidOperationException(
                "Set ASSISTANT_SSH_HOST to an SSH config alias. Do not commit a host address.");

    private const int MaxChars = 8000;

    public async Task<RemoteOutput> RunAsync(RemoteRequest request, CancellationToken cancellationToken)
    {
        foreach (var argument in request.Arguments)
        {
            if (!ScriptArguments.IsSafe(argument))
            {
                throw new InvalidOperationException("An argument was rejected before the remote call.");
            }
        }

        var script = EmbeddedScripts.Load(request.ScriptName);
        var start = new ProcessStartInfo
        {
            FileName = "ssh",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        start.ArgumentList.Add("-o");
        start.ArgumentList.Add("BatchMode=yes");
        start.ArgumentList.Add("-o");
        start.ArgumentList.Add("ConnectTimeout=10");
        start.ArgumentList.Add(HostAlias);
        start.ArgumentList.Add("bash");
        start.ArgumentList.Add("-s");
        start.ArgumentList.Add("--");
        foreach (var argument in request.Arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = start };
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("ssh did not start. ASSISTANT_SSH_HOST must name an SSH config alias.");
            }
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            throw new InvalidOperationException(
                "ssh did not start. ASSISTANT_SSH_HOST must name an SSH config alias.",
                exception);
        }

        using var timeout = new CancellationTokenSource(request.Timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var stdoutTask = process.StandardOutput.ReadToEndAsync(linked.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(linked.Token);
        await process.StandardInput.WriteAsync(script.AsMemory(), linked.Token).ConfigureAwait(false);
        process.StandardInput.Close();

        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            await ObserveAsync(stdoutTask, stderrTask).ConfigureAwait(false);
            return new RemoteOutput(124, string.Empty, "The remote command timed out.");
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        var stdout = Cap(await stdoutTask.ConfigureAwait(false));
        var stderr = Cap(await stderrTask.ConfigureAwait(false));
        return new RemoteOutput(process.ExitCode, stdout, stderr);
    }

    private static async Task ObserveAsync(Task<string> stdout, Task<string> stderr)
    {
        try
        {
            await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }
    }
    private static string Cap(string value) =>
        value.Length <= MaxChars ? value : value[..MaxChars];

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }
}
