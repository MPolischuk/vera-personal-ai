namespace VpsOperator.Mcp;

internal sealed record RemoteRequest(string ScriptName, IReadOnlyList<string> Arguments, TimeSpan Timeout);

internal sealed record RemoteOutput(int ExitCode, string Stdout, string Stderr);

internal interface IRemoteShell
{
    public Task<RemoteOutput> RunAsync(RemoteRequest request, CancellationToken cancellationToken);
}
