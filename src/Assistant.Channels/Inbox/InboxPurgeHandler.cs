using Assistant.Abstractions.Channels;
using Assistant.Abstractions.Scheduling;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Assistant.Channels.Inbox;

/// <summary>
/// Job <c>channel.inbox.purge</c>. Deletes old dedup keys and reschedules itself for the next 04:10.
/// </summary>
public sealed class InboxPurgeHandler(
    IChannelInboxStore store,
    TimeProvider clock,
    IOptions<InboxOptions> options,
    ILogger<InboxPurgeHandler> logger) : IJobHandler
{
    public const string KindName = "channel.inbox.purge";

    public string Kind => KindName;

    public async ValueTask<JobOutcome> ExecuteAsync(JobContext context, CancellationToken ct)
    {
        foreach (var (channel, retention) in options.Value.InboxRetention)
        {
            if (string.IsNullOrWhiteSpace(channel) || retention <= TimeSpan.Zero)
            {
                continue;
            }

            var removed = await store.PurgeAsync(new ChannelId(channel), retention, ct);
            InboxLog.Purged(logger, removed, channel);
        }

        return new JobOutcome.Reschedule(InboxPurgeSchedule.NextRun(clock.GetUtcNow()));
    }
}
