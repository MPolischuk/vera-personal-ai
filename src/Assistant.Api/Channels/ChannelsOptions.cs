namespace Assistant.Api.Channels;

/// <summary>
/// <c>Channels</c> section. Remote rules are read by <see cref="RemoteChannelPolicy"/>.
/// The limits are the stage defaults; pairing and the inbox consume them later.
/// </summary>
internal sealed class ChannelsOptions
{
    public const string SectionName = "Channels";

    public string PublicBaseUrl { get; set; } = "";

    public int MaxApprovedPeersPerChannel { get; set; } = 1;

    public int MaxPendingPeers { get; set; } = 3;

    public int MaxPairingAttempts { get; set; } = 5;

    public TimeSpan PairingCodeTtl { get; set; } = TimeSpan.FromHours(1);

    public TimeSpan DeepLinkTokenTtl { get; set; } = TimeSpan.FromMinutes(10);

    public TimeSpan PairingReminderInterval { get; set; } = TimeSpan.FromMinutes(10);

    public int InboxParallelism { get; set; } = 4;

    public RemoteChannelOptions Remote { get; set; } = new();
}

internal sealed class RemoteChannelOptions
{
    public string[] WebOnlyTools { get; set; } = [];

    public bool AllowSensitiveActions { get; set; } = true;
}
