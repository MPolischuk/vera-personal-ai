namespace Assistant.Channels.Pairing;

/// <summary><c>Channels</c> pairing limits. The inbox reads other keys of the same section.</summary>
public sealed class PairingOptions
{
    public const string SectionName = "Channels";

    public int MaxApprovedPeersPerChannel { get; set; } = 1;

    public int MaxPendingPeers { get; set; } = 3;

    public int MaxPairingAttempts { get; set; } = 5;

    public TimeSpan PairingCodeTtl { get; set; } = TimeSpan.FromHours(1);

    public TimeSpan DeepLinkTokenTtl { get; set; } = TimeSpan.FromMinutes(10);

    public TimeSpan PairingReminderInterval { get; set; } = TimeSpan.FromMinutes(10);
}
