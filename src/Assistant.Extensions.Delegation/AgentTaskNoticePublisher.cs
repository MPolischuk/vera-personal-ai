using Assistant.Abstractions.Notifications;
using Assistant.Abstractions.Tasks;

namespace Assistant.Extensions.Delegation;

/// <summary>Publishes armed finish notices once. A second pass sees <c>notice_armed</c> already false.</summary>
internal static class AgentTaskNoticePublisher
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static async ValueTask PublishArmedAsync(
        IAgentTaskStore store,
        INotificationPublisher publisher,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try
        {
            var armed = await store.ListArmedNoticesAsync(ct);
            foreach (var notice in armed)
            {
                await publisher.PublishAsync(AgentTaskNotices.Create(notice), ct);
                await store.DisarmNoticeAsync(notice.Id, now, ct);
            }
        }
        finally
        {
            Gate.Release();
        }
    }
}
