using System.ComponentModel.DataAnnotations;

namespace Assistant.Core.Personality;

public sealed class PersonalityOptions
{
    public const string SectionName = "Assistant:Personality";

    [Required]
    public string ProfilePath { get; set; } = "config/personality.yaml";
}
