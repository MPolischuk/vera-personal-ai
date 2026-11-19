using System.Net.Sockets;

namespace VpsOperator.Mcp;

internal interface ITcpProbe
{
    public Task<string> ProbeAsync(string host, int port, CancellationToken cancellationToken);
}

internal sealed class TcpProbe : ITcpProbe
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    public async Task<string> ProbeAsync(string host, int port, CancellationToken cancellationToken)
    {
        using var client = new TcpClient();
        using var timeout = new CancellationTokenSource(Timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            await client.ConnectAsync(host, port, linked.Token).ConfigureAwait(false);
            return "open";
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return "timeout";
        }
        catch (SocketException)
        {
            return "closed";
        }
    }
}
