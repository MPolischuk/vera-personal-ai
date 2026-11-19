namespace Assistant.Abstractions.Channels;

/// <summary>
/// Whether a user can be reached on a channel.
/// </summary>
/// <param name="Reachable">Whether delivery is possible right now.</param>
/// <param name="TargetRef">Destination reference (chat_id, subscription id, wa_id, and so on).</param>
/// <param name="InsideServiceWindow">Whether the service window is open. Always true except for channels with a <c>ServiceWindow</c>.</param>
/// <param name="Reason">Why the user is unreachable (for example, "no_subscription", "peer_blocked").</param>
public sealed record ChannelReachability(
    bool Reachable,
    string? TargetRef,
    bool InsideServiceWindow,
    string? Reason);
