namespace Assistant.Abstractions.Channels;

/// <summary>
/// Origin of a turn (channel plus peer).
/// </summary>
/// <param name="Channel">Channel the turn entered through.</param>
/// <param name="PeerId">Peer id (null for the hub).</param>
public sealed record TurnOrigin(ChannelId Channel, Guid? PeerId);

/// <summary>
/// Scoped accessor for the origin of the current turn.
/// </summary>
public interface ITurnOriginAccessor
{
    /// <summary>Origin of the current turn (default: hub).</summary>
    public TurnOrigin Current { get; }

    /// <summary>Sets the origin of the turn.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Naming",
        "CA1716:Identifiers should not match keywords",
        Justification = "The channel contract names this member Set. Renaming it would change the public shape.")]
    public void Set(TurnOrigin origin);
}
