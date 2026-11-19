using System.Data;
using System.Data.Common;
using System.Text.Json;
using Assistant.Abstractions.Notifications;
using Assistant.Abstractions.Reminders;
using Assistant.Abstractions.Scheduling;
using Assistant.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Assistant.Persistence.Reminders;

/// <summary>Writes that <see cref="ReminderFiringStore.CommitFiringAsync"/> performs, in order.</summary>
internal enum ReminderFiringStep
{
    /// <summary>Insert the notification row.</summary>
    InsertNotification = 1,

    /// <summary>Schedule <c>notification.dispatch</c>.</summary>
    ScheduleDispatch = 2,

    /// <summary>Update the reminder row.</summary>
    UpdateReminder = 3,

    /// <summary>Schedule the next <c>reminder.fire</c>.</summary>
    ScheduleNextFire = 4,
}

/// <summary>
/// Fires a reminder in one database transaction: notification, dispatch job,
/// reminder update, and the next fire job commit together or not at all.
/// <see cref="LoadForFiringAsync"/> locks the row with <c>FOR UPDATE</c> until commit,
/// rollback, or dispose.
/// </summary>
internal sealed class ReminderFiringStore(AssistantDbContext db, IJobScheduler scheduler, TimeProvider clock)
    : IReminderFiringStore, IAsyncDisposable
{
    public const string DispatchKind = "notification.dispatch";

    public const int DispatchMaxAttempts = 5;

    private static readonly JsonSerializerOptions ActionJson = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly JsonSerializerOptions DispatchJson = new(JsonSerializerDefaults.Web);

    private IDbContextTransaction? _transaction;

    /// <summary>
    /// Test seam. Invoked before each write so a throw leaves the transaction uncommitted.
    /// </summary>
    internal Action<ReminderFiringStep>? BeforeStep { get; set; }

    public async ValueTask<ReminderFiringSnapshot?> LoadForFiringAsync(Guid reminderId, CancellationToken ct)
    {
        await BeginAsync(ct);
        try
        {
            await LockAsync(reminderId, ct);
            var entity = await db.Reminders.FirstOrDefaultAsync(row => row.Id == reminderId, ct);
            if (entity is null || !string.Equals(entity.Status, ReminderStatus.Active, StringComparison.Ordinal))
            {
                await AbandonAsync();
                return null;
            }

            return ToSnapshot(entity);
        }
        catch
        {
            await AbandonAsync();
            throw;
        }
    }

    public async ValueTask CommitFiringAsync(ReminderFiring firing, CancellationToken ct)
    {
        await BeginAsync(ct);
        try
        {
            ArgumentNullException.ThrowIfNull(firing);
            if (firing.NextOccurrenceUtc is null
                && firing.FinalStatusIfNoNext is not (ReminderStatus.Completed or ReminderStatus.Missed))
            {
                throw new ArgumentException(
                    "Final status must be completed or missed when there is no next occurrence.",
                    nameof(firing));
            }

            await LockAsync(firing.ReminderId, ct);
            var entity = db.Reminders.Local.FirstOrDefault(row => row.Id == firing.ReminderId)
                ?? await db.Reminders.FirstOrDefaultAsync(row => row.Id == firing.ReminderId, ct);
            if (entity is null)
            {
                throw new InvalidOperationException("The reminder to fire was not found.");
            }

            if (!string.Equals(entity.Status, ReminderStatus.Active, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The reminder is not active.");
            }

            var occurrence = ReminderFire.NormalizeUtc(firing.OccurrenceUtc);
            if (entity.NextOccurrenceAt is not DateTimeOffset due
                || ReminderFire.NormalizeUtc(due) != occurrence)
            {
                throw new InvalidOperationException(
                    "The firing occurrence does not match the stored reminder.");
            }

            if (firing.Notification is not null)
            {
                var notificationId = await InsertNotificationAsync(firing.Notification, ct);
                Before(ReminderFiringStep.ScheduleDispatch);
                await scheduler.ScheduleAsync(DispatchSpec(notificationId, firing.Notification), ct);
            }

            Before(ReminderFiringStep.UpdateReminder);
            ApplyReminder(entity, firing, occurrence);
            await db.SaveChangesAsync(ct);

            if (firing.NextOccurrenceUtc is DateTimeOffset next)
            {
                Before(ReminderFiringStep.ScheduleNextFire);
                var normalized = ReminderFire.NormalizeUtc(next);
                await scheduler.ScheduleAsync(
                    new JobSpec(
                        ReminderFire.JobKind,
                        normalized,
                        ReminderFire.PayloadJson(firing.ReminderId, normalized),
                        ReminderFire.IdempotencyKey(firing.ReminderId, normalized)),
                    ct);
            }

            await CommitAsync(ct);
        }
        catch
        {
            await AbandonAsync();
            throw;
        }
    }

    public async ValueTask<ReminderFiringSnapshot?> LoadAsync(Guid reminderId, CancellationToken ct)
    {
        var entity = await db.Reminders.AsNoTracking()
            .FirstOrDefaultAsync(row => row.Id == reminderId, ct);
        return entity is null ? null : ToSnapshot(entity);
    }

    public async ValueTask<NotificationActionResult> ApplyNotificationActionAsync(
        Guid userId,
        Guid notificationId,
        string actionId,
        int maxSnoozes,
        TimeSpan snoozeDelay,
        CancellationToken ct)
    {
        if (actionId is not ("done" or "opened" or ReminderSnooze.ActionId))
        {
            return new NotificationActionResult(NotificationActionStatus.Unsupported, null);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockNotificationAsync(notificationId, ct);
        var notification = await db.Notifications.FirstOrDefaultAsync(row => row.Id == notificationId, ct);
        if (notification is null || notification.UserId != userId)
        {
            return new NotificationActionResult(NotificationActionStatus.NotFound, null);
        }

        var channel = notification.PreferredChannel;
        if (!string.IsNullOrEmpty(notification.ActionTaken)
            || notification.Status == NotificationStatuses.Acted)
        {
            return new NotificationActionResult(NotificationActionStatus.AlreadyActed, channel);
        }

        if (actionId == ReminderSnooze.ActionId)
        {
            var snoozed = await SnoozeAsync(notification, userId, maxSnoozes, snoozeDelay, ct);
            if (snoozed != NotificationActionStatus.Applied)
            {
                return new NotificationActionResult(snoozed, channel);
            }
        }
        else if (actionId == "done")
        {
            await CompleteSingleAsync(notification, userId, ct);
        }

        notification.ActionTaken = actionId;
        notification.ActedAt = clock.GetUtcNow();
        notification.Status = NotificationStatuses.Acted;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new NotificationActionResult(NotificationActionStatus.Applied, channel);
    }

    public async ValueTask DisposeAsync() => await AbandonAsync();

    private async Task<NotificationActionStatus> SnoozeAsync(
        NotificationEntity notification,
        Guid userId,
        int maxSnoozes,
        TimeSpan snoozeDelay,
        CancellationToken ct)
    {
        if (!Guid.TryParse(notification.SourceId, out var reminderId)
            || !string.Equals(notification.SourceType, "reminder", StringComparison.Ordinal))
        {
            return NotificationActionStatus.Unsupported;
        }

        await LockAsync(reminderId, ct);
        var reminder = await db.Reminders.FirstOrDefaultAsync(
            row => row.Id == reminderId && row.UserId == userId, ct);
        if (reminder is null || reminder.Status == ReminderStatus.Cancelled)
        {
            return NotificationActionStatus.NotFound;
        }

        if (reminder.SnoozeCount >= maxSnoozes)
        {
            return NotificationActionStatus.SnoozeLimit;
        }

        reminder.SnoozeCount += 1;
        var runAt = clock.GetUtcNow().Add(snoozeDelay);
        await scheduler.ScheduleAsync(
            new JobSpec(
                ReminderSnooze.JobKind,
                runAt,
                ReminderSnooze.PayloadJson(reminder.Id, reminder.SnoozeCount),
                ReminderSnooze.IdempotencyKey(reminder.Id, reminder.SnoozeCount)),
            ct);
        return NotificationActionStatus.Applied;
    }

    private async Task CompleteSingleAsync(NotificationEntity notification, Guid userId, CancellationToken ct)
    {
        if (!Guid.TryParse(notification.SourceId, out var reminderId)
            || !string.Equals(notification.SourceType, "reminder", StringComparison.Ordinal))
        {
            return;
        }

        await LockAsync(reminderId, ct);
        var reminder = await db.Reminders.FirstOrDefaultAsync(
            row => row.Id == reminderId && row.UserId == userId, ct);
        if (reminder is null
            || reminder.Status != ReminderStatus.Active
            || !string.IsNullOrWhiteSpace(reminder.RecurrenceRule))
        {
            return;
        }

        if (reminder.NextOccurrenceAt is DateTimeOffset due)
        {
            await scheduler.CancelAsync(ReminderFire.IdempotencyKey(reminder.Id, due), ct);
        }

        reminder.Status = ReminderStatus.Completed;
        reminder.CompletedAt = clock.GetUtcNow();
        reminder.NextOccurrenceAt = null;
    }

    private async Task LockNotificationAsync(Guid notificationId, CancellationToken ct)
    {
        const string Sql = "SELECT id FROM notifications WHERE id = $1 FOR UPDATE";
        await using var command = await CreateCommandAsync(Sql, ct);
        command.Parameters.Add(new NpgsqlParameter { Value = notificationId });
        await command.ExecuteScalarAsync(ct);
    }

    private async Task<Guid> InsertNotificationAsync(NotificationRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        Before(ReminderFiringStep.InsertNotification);
        var entity = new NotificationEntity
        {
            Id = Guid.CreateVersion7(),
            UserId = request.UserId,
            Kind = NotificationKindNames.ToWire(request.Kind),
            DedupKey = request.DedupKey,
            Title = request.Title,
            Body = request.Body,
            Trust = request.Trust.ToString().ToLowerInvariant(),
            DueAt = request.DueAt,
            PreferredChannel = request.PreferredChannel?.Value,
            ConversationId = request.ConversationId,
            Actions = request.Actions is { Count: > 0 }
                ? JsonSerializer.Serialize(request.Actions, ActionJson)
                : null,
            SourceType = request.SourceType,
            SourceId = request.SourceId,
            Status = NotificationStatuses.Pending,
        };
        db.Notifications.Add(entity);
        await db.SaveChangesAsync(ct);
        return entity.Id;
    }

    private JobSpec DispatchSpec(Guid notificationId, NotificationRequest request)
        => new(
            DispatchKind,
            clock.GetUtcNow(),
            JsonSerializer.Serialize(
                new DispatchPayload(notificationId, request.AllowDuringQuietHours),
                DispatchJson),
            IdempotencyKey(notificationId),
            DispatchMaxAttempts);

    internal static string IdempotencyKey(Guid notificationId)
        => FormattableString.Invariant($"notification:{notificationId:D}:dispatch");

    private void ApplyReminder(ReminderEntity entity, ReminderFiring firing, DateTimeOffset occurrence)
    {
        if (firing.RecurrenceRule is not null
            && !string.Equals(entity.RecurrenceRule, firing.RecurrenceRule, StringComparison.Ordinal))
        {
            entity.RecurrenceRule = firing.RecurrenceRule;
        }

        var now = clock.GetUtcNow();
        if (firing.Notification is not null)
        {
            entity.OccurrencesFired += 1;
            entity.LastFiredAt = now;
        }

        if (firing.NextOccurrenceUtc is DateTimeOffset next)
        {
            entity.Status = ReminderStatus.Active;
            entity.NextOccurrenceAt = ReminderFire.NormalizeUtc(next);
            entity.SnoozeCount = 0;
            return;
        }

        entity.NextOccurrenceAt = null;
        entity.Status = firing.FinalStatusIfNoNext;
        if (string.Equals(firing.FinalStatusIfNoNext, ReminderStatus.Completed, StringComparison.Ordinal))
        {
            entity.CompletedAt = now;
            if (firing.Notification is null)
            {
                entity.OccurrencesFired += 1;
                entity.LastFiredAt = occurrence;
            }
        }
    }

    private void Before(ReminderFiringStep step) => BeforeStep?.Invoke(step);

    private async Task BeginAsync(CancellationToken ct)
    {
        if (_transaction is not null)
        {
            return;
        }

        _transaction = await db.Database.BeginTransactionAsync(ct);
    }

    private async Task CommitAsync(CancellationToken ct)
    {
        var transaction = _transaction
            ?? throw new InvalidOperationException("The firing transaction is not open.");
        _transaction = null;
        await transaction.CommitAsync(ct);
        await transaction.DisposeAsync();
    }

    private async Task AbandonAsync()
    {
        var transaction = _transaction;
        _transaction = null;
        if (transaction is null)
        {
            return;
        }

        try
        {
            await transaction.RollbackAsync();
        }
        catch (Exception)
        {
            // The original failure is the one the caller must see.
        }

        await transaction.DisposeAsync();
        db.ChangeTracker.Clear();
    }

    private async Task LockAsync(Guid reminderId, CancellationToken ct)
    {
        const string Sql = "SELECT id FROM reminders WHERE id = $1 FOR UPDATE";
        await using var command = await CreateCommandAsync(Sql, ct);
        command.Parameters.Add(new NpgsqlParameter { Value = reminderId });
        await command.ExecuteScalarAsync(ct);
    }

    private async Task<DbCommand> CreateCommandAsync(string sql, CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }

        var command = connection.CreateCommand();
        command.CommandText = sql;
        if (db.Database.CurrentTransaction is { } transaction)
        {
            command.Transaction = transaction.GetDbTransaction();
        }

        return command;
    }

    private static ReminderFiringSnapshot ToSnapshot(ReminderEntity entity)
        => new(
            entity.Id,
            entity.UserId,
            entity.Text,
            entity.Status,
            entity.NextOccurrenceAt,
            entity.RecurrenceRule,
            entity.TimeZone,
            DateTime.SpecifyKind(entity.LocalStart, DateTimeKind.Unspecified),
            entity.OccurrencesFired,
            entity.OriginChannel,
            entity.OriginConversationId,
            entity.SnoozeCount);

    private sealed record DispatchPayload(Guid NotificationId, bool AllowDuringQuietHours);
}
