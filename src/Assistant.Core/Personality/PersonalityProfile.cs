namespace Assistant.Core.Personality;

public sealed record PersonalityProfile
{
    public string Name { get; init; } = string.Empty;

    /// <summary>BCP-47 culture. Drives reply language and day names in the temporal context.</summary>
    public string Language { get; init; } = "es-AR";

    /// <summary>IANA identifier. See D9: the process runs in UTC and this is the user's local time.</summary>
    public string TimeZone { get; init; } = "UTC";

    public string? Relationship { get; init; }

    public PersonalityStyle Style { get; init; } = new();

    public IReadOnlyList<string> Traits { get; init; } = [];

    public IReadOnlyList<string> Boundaries { get; init; } = [];

    /// <summary>Free-form text appended at the end of the prompt. Escape hatch, not a replacement (D7).</summary>
    public string? Instructions { get; init; }

    /// <summary>
    /// Character catalog. Empty by default; never null. Likes/dislikes stay out of the stable prompt (ADR-0013).
    /// </summary>
    public CharacterCatalog Character { get; init; } = CharacterCatalog.Empty;

    // Declared and stored; nothing reads them until 1b and 1c.
    public VoiceSettings Voice { get; init; } = new();

    public AvatarSettings Avatar { get; init; } = new();
}

public sealed record PersonalityStyle
{
    public FormalityLevel Formality { get; init; } = FormalityLevel.Medium;
    public HumorLevel Humor { get; init; } = HumorLevel.Moderate;
    public VerbosityLevel Verbosity { get; init; } = VerbosityLevel.Balanced;
    public EmojiUse Emoji { get; init; } = EmojiUse.Never;
}

public enum FormalityLevel { Low, Medium, High }
public enum HumorLevel { None, Moderate, High }
public enum VerbosityLevel { Concise, Balanced, Detailed }
public enum EmojiUse { Never, Occasional, Frequent }

public sealed record VoiceSettings
{
    public string Provider { get; init; } = "default";
    public string VoiceId { get; init; } = string.Empty;
    public double Speed { get; init; } = 1.0;
}

public sealed record AvatarSettings
{
    public string Model { get; init; } = "avatars/default.vrm";
    public string IdleAnimation { get; init; } = "idle";
}
