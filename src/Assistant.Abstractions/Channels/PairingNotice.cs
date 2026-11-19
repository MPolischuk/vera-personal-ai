namespace Assistant.Abstractions.Channels;

/// <summary>Fixed pairing text a channel messenger knows how to render. The code is not logged.</summary>
public abstract record PairingNotice
{
    /// <summary>First time the sender sees a code.</summary>
    /// <param name="Code">Display form <c>XXXX-XXXX</c>.</param>
    /// <param name="ExpiresAt">When the code expires.</param>
    public sealed record NewCode(string Code, DateTimeOffset ExpiresAt) : PairingNotice;

    /// <summary>Repeat of the current code.</summary>
    /// <param name="Code">Display form <c>XXXX-XXXX</c>.</param>
    public sealed record Reminder(string Code) : PairingNotice;

    /// <summary>The peer was just approved.</summary>
    public sealed record Welcome : PairingNotice;

    /// <summary>The deep link is missing, expired, or already used.</summary>
    public sealed record LinkRejected : PairingNotice;
}

/// <summary>Sends a pairing notice on one channel. Implementations must not log the notice.</summary>
public interface IPairingMessenger
{
    /// <summary>Channel this messenger sends on.</summary>
    public ChannelId Channel { get; }

    /// <summary>Sends one notice to the external peer.</summary>
    public ValueTask SendAsync(string externalPeerId, PairingNotice notice, CancellationToken ct);
}
