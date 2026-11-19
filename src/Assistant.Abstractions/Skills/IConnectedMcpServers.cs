namespace Assistant.Abstractions.Skills;

public interface IConnectedMcpServers
{
    public bool IsConnected(string serverId);
}
