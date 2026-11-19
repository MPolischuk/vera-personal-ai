using System.Globalization;
using System.Text.Json;
using Assistant.Abstractions.Reminders;
using Assistant.Abstractions.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace Assistant.Persistence.Reminders;

/// <summary>
/// Reminder rows and their <c>reminder.fire</c> jobs share the scoped context.
/// The scheduler joins the context's current transaction, so the row and the job
/// commit or roll back together.
/// </summary>
internal sealed class ReminderStore(AssistantDbContext db, IJobScheduler scheduler, TimeProvider clock)
    : IReminderStore, IReminderQueryService
{
    private const int MaxTextLength = 500;

    private static readonly JsonSerializerOptions ProvenanceOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public async ValueTask<CreateReminderResult> CreateAsync(
        NewReminder reminder, int maxActive, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(reminder);
        if (maxActive < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxActive), "maxActive must be at least 1.");
        }

        if (string.IsNullOrWhiteSpace(reminder.Text) || reminder.Text.Length > MaxTextLength)
        {
            throw new ArgumentException(
                "Reminder text must be between 1 and 500 characters.", nameof(reminder));
        }

        if (string.IsNullOrWhiteSpace(reminder.OriginChannel))
        {
            throw new ArgumentException("Origin channel is required.", nameof(reminder));
        }

        var occurrence = ReminderFire.NormalizeUtc(reminder.NextOccurrenceAt);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var active = await db.Reminders.CountAsync(
            row => row.UserId == reminder.UserId && row.Status == ReminderStatus.Active,
            ct);
        if (active >= maxActive)
        {
            return new CreateReminderResult.Rejected(ReminderRejection.TooManyActive);
        }

        var entity = new ReminderEntity
        {
            Id = Guid.CreateVersion7(),
            UserId = reminder.UserId,
            Text = reminder.Text,
            Status = ReminderStatus.Active,
            TimeZone = reminder.TimeZone,
            LocalStart = DateTime.SpecifyKind(reminder.LocalStart, DateTimeKind.Unspecified),
            NextOccurrenceAt = occurrence,
            RecurrenceRule = reminder.RecurrenceRule,
            OccurrencesFired = 0,
            OriginChannel = reminder.OriginChannel,
            OriginConversationId = reminder.OriginConversationId,
            Provenance = JsonSerializer.Serialize(reminder.Provenance, ProvenanceOptions),
            SnoozeCount = 0,
        };
        db.Reminders.Add(entity);
        await db.SaveChangesAsync(ct);

        await scheduler.ScheduleAsync(
            new JobSpec(
                ReminderFire.JobKind,
                occurrence,
                ReminderFire.PayloadJson(entity.Id, occurrence),
                ReminderFire.IdempotencyKey(entity.Id, occurrence)),
            ct);

        var shortRef = await ShortRefAsync(reminder.UserId, entity.Id, ct);
        await transaction.CommitAsync(ct);
        return new CreateReminderResult.Created(ToModel(entity), shortRef);
    }

    public async ValueTask<CancelReminderResult> CancelAsync(
        Guid userId, Guid reminderId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var entity = await db.Reminders
            .FirstOrDefaultAsync(row => row.Id == reminderId && row.UserId == userId, ct);
        if (entity is null)
        {
            return new CancelReminderResult.Rejected(ReminderRejection.NotFound);
        }

        if (!string.Equals(entity.Status, ReminderStatus.Active, StringComparison.Ordinal))
        {
            return new CancelReminderResult.Rejected(ReminderRejection.NotActive);
        }

        var now = clock.GetUtcNow();
        entity.Status = ReminderStatus.Cancelled;
        entity.CancelledAt = now;
        if (entity.NextOccurrenceAt is DateTimeOffset occurrence)
        {
            await scheduler.CancelAsync(ReminderFire.IdempotencyKey(entity.Id, occurrence), ct);
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new CancelReminderResult.Cancelled(ToModel(entity));
    }

    public async ValueTask<IReadOnlyList<ActiveReminder>> ListActiveAsync(Guid userId, CancellationToken ct)
    {
        var items = await ListAsync(userId, ReminderListScope.Active, 50, ct);
        var labeled = new List<ActiveReminder>(items.Count);
        foreach (var item in items)
        {
            labeled.Add(new ActiveReminder(
                item.ShortRef ?? 0,
                item.Text,
                LocalDueLabel(item)));
        }

        return labeled;
    }

    public async ValueTask<IReadOnlyList<ReminderListItem>> ListAsync(
        Guid userId, ReminderListScope scope, int limit, CancellationToken ct)
    {
        if (limit < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "limit must be at least 1.");
        }

        var actives = await ActiveOrdered(userId).ToListAsync(ct);
        var refs = new Dictionary<Guid, int>(actives.Count);
        for (var index = 0; index < actives.Count; index++)
        {
            refs[actives[index].Id] = index + 1;
        }

        IReadOnlyList<ReminderEntity> rows = scope switch
        {
            ReminderListScope.Active => actives,
            ReminderListScope.Missed => await MissedOrdered(userId).ToListAsync(ct),
            ReminderListScope.All => actives
                .Concat(await MissedOrdered(userId).ToListAsync(ct))
                .ToList(),
            _ => throw new ArgumentOutOfRangeException(nameof(scope), "Unknown reminder list scope."),
        };

        return rows.Take(limit).Select(row => ToItem(row, refs)).ToList();
    }

    public async ValueTask<Guid?> FindActiveIdByShortRefAsync(
        Guid userId, int shortRef, CancellationToken ct)
    {
        if (shortRef < 1)
        {
            return null;
        }

        var ids = await ActiveOrdered(userId)
            .Skip(shortRef - 1)
            .Take(1)
            .Select(row => (Guid?)row.Id)
            .ToListAsync(ct);
        return ids.Count == 0 ? null : ids[0];
    }

    private async Task<int> ShortRefAsync(Guid userId, Guid reminderId, CancellationToken ct)
    {
        var ids = await ActiveOrdered(userId).Select(row => row.Id).ToListAsync(ct);
        var index = ids.IndexOf(reminderId);
        if (index < 0)
        {
            throw new InvalidOperationException("The new reminder is missing from the active list.");
        }

        return index + 1;
    }

    private IQueryable<ReminderEntity> ActiveOrdered(Guid userId)
        => db.Reminders
            .Where(row => row.UserId == userId && row.Status == ReminderStatus.Active)
            .OrderBy(row => row.NextOccurrenceAt)
            .ThenBy(row => row.Id);

    private IQueryable<ReminderEntity> MissedOrdered(Guid userId)
        => db.Reminders
            .Where(row => row.UserId == userId && row.Status == ReminderStatus.Missed)
            .OrderByDescending(row => row.NextOccurrenceAt)
            .ThenBy(row => row.Id);

    private string LocalDueLabel(ReminderListItem item)
    {
        if (item.NextOccurrenceAt is not DateTimeOffset next)
        {
            return "";
        }

        if (!TimeZoneInfo.TryFindSystemTimeZoneById(item.TimeZone, out var zone))
        {
            zone = TimeZoneInfo.Utc;
        }

        var local = TimeZoneInfo.ConvertTime(next, zone);
        var today = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone).Date;
        var time = local.ToString("HH:mm", CultureInfo.InvariantCulture);
        if (local.Date == today)
        {
            return "hoy " + time;
        }

        if (local.Date == today.AddDays(1))
        {
            return "mañana " + time;
        }

        return FormattableString.Invariant(
            $"{Weekday(local.DayOfWeek)} {local.Day}/{local.Month} {time}");
    }

    private static string Weekday(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "lunes",
        DayOfWeek.Tuesday => "martes",
        DayOfWeek.Wednesday => "miércoles",
        DayOfWeek.Thursday => "jueves",
        DayOfWeek.Friday => "viernes",
        DayOfWeek.Saturday => "sábado",
        DayOfWeek.Sunday => "domingo",
        _ => "día",
    };

    private static ReminderListItem ToItem(ReminderEntity row, Dictionary<Guid, int> refs)
        => new(
            row.Id,
            refs.TryGetValue(row.Id, out var shortRef) ? shortRef : null,
            row.Text,
            row.Status,
            row.TimeZone,
            DateTime.SpecifyKind(row.LocalStart, DateTimeKind.Unspecified),
            row.NextOccurrenceAt,
            row.RecurrenceRule,
            row.OriginChannel,
            row.UpdatedAt);

    private static Reminder ToModel(ReminderEntity row)
    {
        var provenance = JsonSerializer.Deserialize<ReminderProvenance>(row.Provenance, ProvenanceOptions)
            ?? throw new InvalidOperationException("Reminder provenance is missing.");
        return new Reminder(
            row.Id,
            row.UserId,
            row.Text,
            row.Status,
            row.TimeZone,
            DateTime.SpecifyKind(row.LocalStart, DateTimeKind.Unspecified),
            row.NextOccurrenceAt,
            row.RecurrenceRule,
            row.OccurrencesFired,
            row.LastFiredAt,
            row.OriginChannel,
            row.OriginConversationId,
            provenance,
            row.SnoozeCount,
            row.CreatedAt,
            row.UpdatedAt,
            row.CancelledAt,
            row.CompletedAt);
    }
}
