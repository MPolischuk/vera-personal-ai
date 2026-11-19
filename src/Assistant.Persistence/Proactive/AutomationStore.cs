using Assistant.Abstractions.Proactive;
using Microsoft.EntityFrameworkCore;

namespace Assistant.Persistence.Proactive;

internal sealed class AutomationStore(AssistantDbContext db) : IAutomationStore
{
    public const string SystemTitle = "Briefing";

    public const string SystemInstruction = "Generá el briefing del día.";

    public const string SystemLocalTime = "08:00";

    public async ValueTask<Automation> InsertUserAsync(NewUserAutomation draft, DateTimeOffset now, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var localTime = ValidateUser(draft);
        var entity = new AutomationEntity
        {
            Id = Guid.CreateVersion7(),
            UserId = draft.UserId,
            Title = draft.Title.Trim(),
            Instruction = draft.Instruction.Trim(),
            Cadence = draft.Cadence,
            LocalTime = localTime,
            IntervalMinutes = draft.Cadence == AutomationCadence.Interval ? draft.IntervalMinutes : null,
            Status = AutomationStatuses.Active,
            ConversationId = draft.ConversationId,
            NextRunAt = draft.RunAt?.ToUniversalTime(),
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Automations.Add(entity);
        await db.SaveChangesAsync(ct);
        return ToModel(entity);
    }

    public async ValueTask<Automation> EnsureSystemBriefingAsync(
        Guid userId, Guid conversationId, DateTimeOffset nextRunAt, CancellationToken ct)
    {
        var existing = await db.Automations
            .SingleOrDefaultAsync(
                automation => automation.UserId == userId && automation.Cadence == AutomationCadence.SystemDaily,
                ct);
        if (existing is not null)
        {
            return ToModel(existing);
        }

        var entity = new AutomationEntity
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            Title = SystemTitle,
            Instruction = SystemInstruction,
            Cadence = AutomationCadence.SystemDaily,
            LocalTime = SystemLocalTime,
            Status = AutomationStatuses.Active,
            ConversationId = conversationId,
            NextRunAt = nextRunAt.ToUniversalTime(),
        };
        db.Automations.Add(entity);
        await db.SaveChangesAsync(ct);
        return ToModel(entity);
    }

    public async ValueTask<Automation?> FindSystemBriefingAsync(Guid userId, CancellationToken ct)
    {
        var entity = await db.Automations.AsNoTracking()
            .SingleOrDefaultAsync(
                automation => automation.UserId == userId && automation.Cadence == AutomationCadence.SystemDaily,
                ct);
        return entity is null ? null : ToModel(entity);
    }

    public async ValueTask<IReadOnlyList<AutomationSummary>> ListAsync(Guid userId, CancellationToken ct)
    {
        return await db.Automations.AsNoTracking()
            .Where(automation => automation.UserId == userId)
            .OrderBy(automation => automation.Title)
            .Select(automation => new AutomationSummary(
                automation.Id,
                automation.Title,
                automation.Cadence,
                automation.Status,
                automation.NextRunAt))
            .ToListAsync(ct);
    }

    public async ValueTask<Automation?> FindAsync(Guid userId, Guid automationId, CancellationToken ct)
    {
        var entity = await db.Automations.AsNoTracking()
            .SingleOrDefaultAsync(automation => automation.Id == automationId && automation.UserId == userId, ct);
        return entity is null ? null : ToModel(entity);
    }

    public async ValueTask<bool> PauseAsync(Guid userId, Guid automationId, DateTimeOffset now, CancellationToken ct)
    {
        var affected = await db.Automations
            .Where(automation => automation.Id == automationId
                && automation.UserId == userId
                && automation.Cadence != AutomationCadence.SystemDaily
                && automation.Status == AutomationStatuses.Active)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(automation => automation.Status, AutomationStatuses.Paused)
                    .SetProperty(automation => automation.UpdatedAt, now),
                ct);
        return affected == 1;
    }

    public async ValueTask<bool> SetNextRunAsync(
        Guid userId,
        Guid automationId,
        DateTimeOffset? nextRunAt,
        string? status,
        CancellationToken ct)
    {
        if (status is not null && status is not (AutomationStatuses.Active or AutomationStatuses.Paused))
        {
            throw new ArgumentException("The automation status is not active or paused.", nameof(status));
        }

        var entity = await db.Automations.SingleOrDefaultAsync(
            automation => automation.Id == automationId && automation.UserId == userId,
            ct);
        if (entity is null || entity.Cadence == AutomationCadence.SystemDaily)
        {
            return false;
        }

        entity.NextRunAt = nextRunAt?.ToUniversalTime();
        if (status is not null)
        {
            entity.Status = status;
        }

        await db.SaveChangesAsync(ct);
        return true;
    }

    public async ValueTask<bool> DeleteAsync(Guid userId, Guid automationId, CancellationToken ct)
    {
        var affected = await db.Automations
            .Where(automation => automation.Id == automationId
                && automation.UserId == userId
                && automation.Cadence != AutomationCadence.SystemDaily)
            .ExecuteDeleteAsync(ct);
        return affected == 1;
    }

    private static string? ValidateUser(NewUserAutomation draft)
    {
        if (!AutomationRules.IsUserCadence(draft.Cadence))
        {
            throw new ArgumentException("The cadence is not once, interval, or daily.", nameof(draft));
        }

        var title = draft.Title?.Trim() ?? string.Empty;
        var instruction = draft.Instruction?.Trim() ?? string.Empty;
        if (title.Length is < 1 or > AutomationRules.TitleMaxLength)
        {
            throw new ArgumentException("The automation title must be 1 to 80 characters.", nameof(draft));
        }

        if (instruction.Length is < 1 or > AutomationRules.InstructionMaxLength)
        {
            throw new ArgumentException("The automation instruction must be 1 to 2000 characters.", nameof(draft));
        }

        if (draft.Cadence == AutomationCadence.Interval
            && (draft.IntervalMinutes is null || draft.IntervalMinutes < AutomationRules.MinIntervalMinutes))
        {
            throw new ArgumentException("The interval must be at least 30 minutes.", nameof(draft));
        }

        if (draft.Cadence == AutomationCadence.Daily)
        {
            if (!AutomationRules.TryParseLocalTime(draft.LocalTime, out var localTime))
            {
                throw new ArgumentException("A daily automation needs a local time HH:mm.", nameof(draft));
            }

            return localTime;
        }

        if (draft.Cadence == AutomationCadence.Once && draft.RunAt is null)
        {
            throw new ArgumentException("A one-shot automation needs a run time.", nameof(draft));
        }

        return null;
    }

    private static Automation ToModel(AutomationEntity entity)
        => new(
            entity.Id,
            entity.UserId,
            entity.Title,
            entity.Instruction,
            entity.Cadence,
            entity.LocalTime,
            entity.IntervalMinutes,
            entity.Status,
            entity.ConversationId,
            entity.NextRunAt);
}
