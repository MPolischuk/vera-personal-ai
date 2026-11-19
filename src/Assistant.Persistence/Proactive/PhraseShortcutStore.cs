using Assistant.Abstractions.Proactive;
using Microsoft.EntityFrameworkCore;

namespace Assistant.Persistence.Proactive;

internal sealed class PhraseShortcutStore(AssistantDbContext db) : IPhraseShortcutStore
{
    public async ValueTask<PhraseShortcut> SetAsync(
        Guid userId, string phraseNormalized, string action, CancellationToken ct)
    {
        if (!PhraseNormalizer.TryNormalize(phraseNormalized, out var normalized)
            || !string.Equals(normalized, phraseNormalized, StringComparison.Ordinal))
        {
            throw new ArgumentException("The phrase is not normalized.", nameof(phraseNormalized));
        }

        if (!string.Equals(action, PhraseActions.BriefingLatest, StringComparison.Ordinal))
        {
            throw new ArgumentException("The phrase action is not supported.", nameof(action));
        }

        var existing = await db.PhraseShortcuts
            .SingleOrDefaultAsync(
                shortcut => shortcut.UserId == userId && shortcut.PhraseNormalized == phraseNormalized,
                ct);
        if (existing is null)
        {
            existing = new PhraseShortcutEntity
            {
                Id = Guid.CreateVersion7(),
                UserId = userId,
                PhraseNormalized = phraseNormalized,
                Action = action,
            };
            db.PhraseShortcuts.Add(existing);
        }
        else
        {
            existing.Action = action;
        }

        await db.SaveChangesAsync(ct);
        return new PhraseShortcut(existing.Id, existing.UserId, existing.PhraseNormalized, existing.Action);
    }

    public async ValueTask<string?> FindActionAsync(Guid userId, string phraseNormalized, CancellationToken ct)
    {
        return await db.PhraseShortcuts.AsNoTracking()
            .Where(shortcut => shortcut.UserId == userId && shortcut.PhraseNormalized == phraseNormalized)
            .Select(shortcut => shortcut.Action)
            .SingleOrDefaultAsync(ct);
    }
}
