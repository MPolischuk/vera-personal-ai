namespace Assistant.Core.Personality;

/// <summary>Where the profile definition comes from. Implemented by the composition root (D2).</summary>
public interface IPersonalityProfileProvider
{
    public ValueTask<PersonalityProfile> LoadAsync(CancellationToken ct);
}
