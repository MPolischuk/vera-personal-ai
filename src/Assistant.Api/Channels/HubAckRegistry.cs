using System.Collections.Concurrent;
using Assistant.Channels.Delivery;

namespace Assistant.Api.Channels;

/// <summary>
/// One <see cref="TaskCompletionSource{T}"/> per armed delivery.
/// A late ack, after the wait removed the entry, is ignored.
/// </summary>
internal sealed class HubAckRegistry : IDeliveryAckWait
{
    private readonly ConcurrentDictionary<Guid, Pending> _pending = new();

    public void Arm(Guid deliveryId, Guid notificationId)
        => _pending[deliveryId] = new Pending(notificationId);

    public async ValueTask<bool> WaitAsync(Guid deliveryId, TimeSpan timeout, CancellationToken ct)
    {
        if (!_pending.TryGetValue(deliveryId, out var pending))
        {
            return false;
        }

        try
        {
            await pending.Completion.Task.WaitAsync(timeout, ct);
            return true;
        }
        catch (TimeoutException)
        {
            _pending.TryRemove(deliveryId, out _);
            return false;
        }
        catch (OperationCanceledException)
        {
            _pending.TryRemove(deliveryId, out _);
            throw;
        }
        finally
        {
            if (pending.Completion.Task.IsCompletedSuccessfully)
            {
                _pending.TryRemove(deliveryId, out _);
            }
        }
    }

    public bool TryCompleteDelivery(Guid deliveryId)
    {
        if (!_pending.TryGetValue(deliveryId, out var pending))
        {
            return false;
        }

        return pending.Completion.TrySetResult(true);
    }

    public bool TryCompleteNotification(Guid notificationId)
    {
        foreach (var pair in _pending)
        {
            if (pair.Value.NotificationId == notificationId)
            {
                return TryCompleteDelivery(pair.Key);
            }
        }

        return false;
    }

    private sealed class Pending(Guid notificationId)
    {
        public Guid NotificationId { get; } = notificationId;

        public TaskCompletionSource<bool> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
