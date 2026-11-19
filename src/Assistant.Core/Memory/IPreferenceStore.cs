namespace Assistant.Core.Memory;

public interface IPreferenceStore
{
    public ValueTask<IReadOnlyList<PreferenceItem>> ListAsync(Guid userId, CancellationToken ct);

    public ValueTask<PreferenceItem> UpsertAsync(
        Guid userId, string key, string value, WriteProvenance writeProvenance, CancellationToken ct);

    public ValueTask<bool> DeleteAsync(Guid userId, string key, CancellationToken ct);
}
