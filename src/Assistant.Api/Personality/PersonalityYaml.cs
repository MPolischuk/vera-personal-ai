namespace Assistant.Api.Personality;

/// <summary>Only the deserializer sees this. Mutable DTO so the core record stays clean (D2).</summary>
internal sealed class PersonalityYaml
{
    public string? Name { get; set; }
    public string? Language { get; set; }
    public string? Timezone { get; set; }
    public string? Relationship { get; set; }
    public StyleYaml? Style { get; set; }
    public List<string>? Traits { get; set; }
    public List<string>? Boundaries { get; set; }
    public string? Instructions { get; set; }
    public VoiceYaml? Voice { get; set; }
    public AvatarYaml? Avatar { get; set; }
    public CharacterYaml? Character { get; set; }
}

internal sealed class StyleYaml
{
    public string? Formality { get; set; }
    public string? Humor { get; set; }
    public string? Verbosity { get; set; }
    public string? Emoji { get; set; }
}

internal sealed class VoiceYaml
{
    public string? Provider { get; set; }
    public string? VoiceId { get; set; }
    public double? Speed { get; set; }
}

internal sealed class AvatarYaml
{
    public string? Model { get; set; }
    public string? IdleAnimation { get; set; }
}

internal sealed class CharacterYaml
{
    public List<CharacterEntryYaml>? Likes { get; set; }
    public List<CharacterEntryYaml>? Dislikes { get; set; }
    public List<QuirkYaml>? Quirks { get; set; }
}

internal sealed class CharacterEntryYaml
{
    public string? Id { get; set; }
    public string? Line { get; set; }
    public List<string>? Tags { get; set; }
}

internal sealed class QuirkYaml
{
    public string? Id { get; set; }
    public string? Line { get; set; }
    public bool? Always { get; set; }
    public List<string>? Tags { get; set; }
}
