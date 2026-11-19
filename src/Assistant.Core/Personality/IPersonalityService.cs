namespace Assistant.Core.Personality;

public interface IPersonalityService
{
    public ValueTask<ActivePersonality> GetActiveAsync(CancellationToken ct);
}

/// <summary>The profile, its already-composed prompt, and its already-estimated cost.</summary>
public sealed record ActivePersonality(PersonalityProfile Profile, string SystemPrompt, int EstimatedTokens);
