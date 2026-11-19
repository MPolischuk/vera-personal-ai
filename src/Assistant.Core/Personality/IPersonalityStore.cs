namespace Assistant.Core.Personality;

public interface IPersonalityStore
{
    /// <summary>
    /// Makes this profile the only active one for the user. Idempotent: if the stored
    /// definition is already identical it does not write, so <c>updated_at</c> reflects
    /// when personality changed, not when the process started.
    /// </summary>
    public ValueTask SaveActiveAsync(
        Guid userId, string profileName, string definitionJson, CancellationToken ct);
}
