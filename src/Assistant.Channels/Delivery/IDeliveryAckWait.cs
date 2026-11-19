namespace Assistant.Channels.Delivery;

/// <summary>
/// In-process wait for a hub ack. Armed before send so an ack that arrives during
/// <c>SendAsync</c> is not lost. A late ack on a removed delivery is ignored.
/// </summary>
public interface IDeliveryAckWait
{
    /// <summary>Starts waiting for <paramref name="deliveryId"/>.</summary>
    public void Arm(Guid deliveryId, Guid notificationId);

    /// <summary>Completes when the ack arrives. False on timeout.</summary>
    public ValueTask<bool> WaitAsync(Guid deliveryId, TimeSpan timeout, CancellationToken ct);

    /// <summary>Completes an armed delivery. False when it is unknown or already closed.</summary>
    public bool TryCompleteDelivery(Guid deliveryId);

    /// <summary>Completes the armed delivery of a notification. False when none is waiting.</summary>
    public bool TryCompleteNotification(Guid notificationId);
}
