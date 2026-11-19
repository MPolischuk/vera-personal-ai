using Assistant.Abstractions.Events;

namespace Assistant.Abstractions.Webhooks;

/// <summary>
/// A signed inbound fact was accepted as a turn. This package does not listen to it.
/// </summary>
/// <param name="UserId">User the peer belongs to.</param>
/// <param name="Source">Webhook source. The ring uses <c>index</c>.</param>
/// <param name="DeliveryId">Delivery id from the signed headers.</param>
/// <param name="Transcript">Transcription that became the turn. Audio is not included.</param>
public sealed record InboundFactReceived(
    Guid UserId,
    string Source,
    string DeliveryId,
    string Transcript) : IDomainEvent;
