namespace Assistant.Abstractions.Channels;

/// <summary>
/// Kind of outbound message.
/// </summary>
public enum OutboundKind
{
    /// <summary>Reply to a turn that entered through this channel.</summary>
    TurnReply,

    /// <summary>Server-initiated notice (reminder, finished task).</summary>
    Notification,

    /// <summary>Confirmation card from the PermissionEngine.</summary>
    ConfirmationRequest,

    /// <summary>Approved template (only channels with <see cref="ChannelCapabilities.RequiresTemplatesOutsideWindow"/>).</summary>
    Template
}

/// <summary>
/// Urgency of an outbound message.
/// </summary>
public enum OutboundUrgency
{
    Low,
    Normal,
    High
}

/// <summary>
/// Interactive action the user can run from the message.
/// </summary>
/// <param name="ActionId">Action identifier (for example, "done", "snooze:10m").</param>
/// <param name="Label">Button text.</param>
public sealed record OutboundAction(string ActionId, string Label);

/// <summary>
/// Pre-approved channel template (WhatsApp).
/// </summary>
/// <param name="Name">Template name.</param>
/// <param name="Language">Language code (for example, "es").</param>
/// <param name="Parameters">Template parameters, in order.</param>
public sealed record OutboundTemplate(
    string Name,
    string Language,
    IReadOnlyList<string> Parameters);

/// <summary>
/// Outbound message to the user on a channel.
/// </summary>
/// <param name="MessageId">Send idempotency key (delivery id or the turn's message id).</param>
/// <param name="Channel">Destination channel.</param>
/// <param name="UserId">Recipient user.</param>
/// <param name="TargetRef">Destination reference (returned by <c>GetReachabilityAsync</c> or the peer binding).</param>
/// <param name="Kind">Message kind.</param>
/// <param name="Text">Message text, already formatted, without a <c>[[cue:…]]</c> preamble.</param>
/// <param name="Title">Title (push).</param>
/// <param name="Urgency">Message urgency.</param>
/// <param name="Silent">Whether to deliver without a sound.</param>
/// <param name="Actions">Interactive buttons.</param>
/// <param name="NotificationId">Notification id (when <c>Kind</c> is <see cref="OutboundKind.Notification"/>).</param>
/// <param name="ConfirmationId">Confirmation id (when <c>Kind</c> is <see cref="OutboundKind.ConfirmationRequest"/>).</param>
/// <param name="ExpiresAt">Expiry time.</param>
/// <param name="DeepLink">Deep link to the conversation.</param>
/// <param name="CollapseKey">Collapse key (push: topic / tag).</param>
/// <param name="ReplyToExternalId">External id of the message being replied to.</param>
/// <param name="Template">Template to use (channels with <see cref="ChannelCapabilities.RequiresTemplatesOutsideWindow"/>).</param>
public sealed record OutboundMessage(
    Guid MessageId,
    ChannelId Channel,
    Guid UserId,
    string TargetRef,
    OutboundKind Kind,
    string Text,
    string? Title = null,
    OutboundUrgency Urgency = OutboundUrgency.Normal,
    bool Silent = false,
    IReadOnlyList<OutboundAction>? Actions = null,
    Guid? NotificationId = null,
    Guid? ConfirmationId = null,
    DateTimeOffset? ExpiresAt = null,
    Uri? DeepLink = null,
    string? CollapseKey = null,
    string? ReplyToExternalId = null,
    OutboundTemplate? Template = null);
