namespace Assistant.Channels.WebPush;

/// <summary><c>WebPush</c> section. Defaults keep the channel off and hold no keys.</summary>
public sealed class WebPushOptions
{
    public const string SectionName = "WebPush";

    public bool Enabled { get; set; }

    /// <summary><c>mailto:</c> of the operator or an <c>https://</c> origin.</summary>
    public string VapidSubject { get; set; } = "";

    /// <summary>Uncompressed P-256 public key, base64url (65 bytes).</summary>
    public string VapidPublicKey { get; set; } = "";

    /// <summary>P-256 private scalar, base64url (32 bytes).</summary>
    public string VapidPrivateKey { get; set; } = "";

    /// <summary>
    /// Hosts that may receive a push. Each entry is an exact host or a <c>*.</c> suffix.
    /// An empty list allows none. When the key is missing, <see cref="DefaultAllowedEndpointHosts"/> applies.
    /// </summary>
    public string[] AllowedEndpointHosts { get; set; } = [..DefaultAllowedEndpointHosts];

    /// <summary>Push services used when <c>WebPush:AllowedEndpointHosts</c> is absent.</summary>
    public static IReadOnlyList<string> DefaultAllowedEndpointHosts { get; } =
    [
        "fcm.googleapis.com",
        "*.push.apple.com",
        "updates.push.services.mozilla.com",
        "*.notify.windows.com",
    ];
}
