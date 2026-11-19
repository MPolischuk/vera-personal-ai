namespace Assistant.Abstractions.Channels;

/// <summary>
/// Result of sending a message on a channel. A closed record hierarchy
/// so switches can be exhaustive.
/// </summary>
public abstract record OutboundResult
{
    /// <summary>Delivered successfully.</summary>
    /// <param name="ExternalId">Message id in the external system, when one exists.</param>
    public sealed record Delivered(string? ExternalId) : OutboundResult;

    /// <summary>Waiting for client acknowledgement (hub).</summary>
    /// <param name="Timeout">How long to wait for the ack.</param>
    public sealed record AwaitingAck(TimeSpan Timeout) : OutboundResult;

    /// <summary>Transient failure; retry.</summary>
    /// <param name="Code">Error code (for example, "rate_limit", "timeout").</param>
    /// <param name="RetryAfter">Suggested delay before retrying.</param>
    public sealed record TransientFailure(string Code, TimeSpan? RetryAfter) : OutboundResult;

    /// <summary>Permanent failure; do not retry.</summary>
    /// <param name="Code">Error code (for example, "403_blocked", "410_gone").</param>
    /// <param name="DeactivateTarget">Whether to deactivate the destination.</param>
    public sealed record PermanentFailure(string Code, bool DeactivateTarget) : OutboundResult;

    /// <summary>Not applicable (for example, a closed window without a template).</summary>
    /// <param name="Reason">Why it does not apply.</param>
    public sealed record NotApplicable(string Reason) : OutboundResult;
}
