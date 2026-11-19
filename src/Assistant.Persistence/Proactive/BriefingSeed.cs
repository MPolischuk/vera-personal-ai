using System.Text.Json;
using Assistant.Abstractions.Proactive;
using Assistant.Abstractions.Scheduling;
using Assistant.Core.Conversations;
using Microsoft.EntityFrameworkCore;

namespace Assistant.Persistence.Proactive;

/// <summary>
/// Ensures each user has the system briefing automation, a job for the next 08:00,
/// and the heartbeat job. The user cannot delete the briefing row from the tool.
/// </summary>
public sealed class BriefingSeed(
    AssistantDbContext db,
    IConversationStore conversations,
    IAutomationStore automations,
    IJobScheduler scheduler,
    TimeProvider clock)
{
    private static readonly JsonSerializerOptions PayloadJson = new(JsonSerializerDefaults.Web);

    public async Task EnsureAsync(CancellationToken ct)
    {
        var userIds = await db.Users.AsNoTracking().Select(user => user.Id).ToListAsync(ct);
        var now = clock.GetUtcNow();
        var runAt = BriefingClock.NextLocalEight(now).ToUniversalTime();
        var localDate = BriefingClock.LocalDate(runAt);
        foreach (var userId in userIds)
        {
            var existing = await automations.FindSystemBriefingAsync(userId, ct);
            if (existing is null)
            {
                var conversation = await conversations.CreateAsync(userId, AutomationStore.SystemTitle, ct);
                existing = await automations.EnsureSystemBriefingAsync(userId, conversation.Id, runAt, ct);
            }

            var payload = JsonSerializer.Serialize(
                new BriefingGeneratePayload(userId, localDate),
                PayloadJson);
            await scheduler.ScheduleAsync(
                new JobSpec(
                    BriefingJobs.GenerateKind,
                    runAt,
                    payload,
                    BriefingJobs.JobKey(userId, localDate)),
                ct);
            var nextHeartbeat = HeartbeatSchedule.Next(
                now,
                SuggestionWindow.Default.QuietStart,
                SuggestionWindow.Default.QuietEnd,
                HeartbeatJobs.EveryMinutes);
            await scheduler.ScheduleAsync(
                new JobSpec(
                    HeartbeatJobs.Kind,
                    nextHeartbeat.ToUniversalTime(),
                    JsonSerializer.Serialize(new HeartbeatJobPayload(userId), PayloadJson),
                    HeartbeatJobs.JobKey(userId)),
                ct);
        }

        var reviewAt = MemoryReviewJobs.NextRun(now);
        var reviewDate = MemoryReviewJobs.LocalDate(reviewAt);
        await scheduler.ScheduleAsync(
            new JobSpec(
                MemoryReviewJobs.Kind,
                reviewAt.ToUniversalTime(),
                JsonSerializer.Serialize(new MemoryReviewPayload(reviewDate), PayloadJson),
                MemoryReviewJobs.JobKey(reviewDate)),
            ct);
    }
}
