namespace Assistant.Channels.Pairing;

/// <summary>Reason strings carried by <see cref="Assistant.Abstractions.Channels.PairingDecision.Silent"/>.</summary>
internal static class PairingReasons
{
    public const string Welcomed = "welcomed";

    public const string Expired = "expired";

    public const string Used = "used";

    public const string Invalid = "invalid";

    public const string Blocked = "blocked";

    public const string Flood = "flood";

    public const string Limit = "peer_limit_reached";
}
