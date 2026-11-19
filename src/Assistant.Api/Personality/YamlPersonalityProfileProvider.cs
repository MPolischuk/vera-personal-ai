using Assistant.Core.Personality;
using Microsoft.Extensions.Options;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Assistant.Api.Personality;

internal sealed class YamlPersonalityProfileProvider(IOptions<PersonalityOptions> options)
    : IPersonalityProfileProvider
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        // Without IgnoreUnmatchedProperties: an unknown key is an error (D10).
        .Build();

    public async ValueTask<PersonalityProfile> LoadAsync(CancellationToken ct)
    {
        var path = PreferLocal(Resolve(options.Value.ProfilePath));

        if (!File.Exists(path))
        {
            throw new PersonalityProfileInvalidException(path, ["The file does not exist."]);
        }

        var text = await File.ReadAllTextAsync(path, ct);

        try
        {
            var yaml = Deserializer.Deserialize<PersonalityYaml>(text)
                ?? throw new PersonalityProfileInvalidException(path, ["The file is empty."]);

            return Map(yaml, path);
        }
        catch (YamlException e)
        {
            var detail = e.InnerException?.Message ?? e.Message;
            throw new PersonalityProfileInvalidException(path, [detail]);
        }
    }

    /// <summary>
    /// Relative to the process output, not the current directory: from the IDE, published,
    /// and in the container the app starts with different directories and the file is copied
    /// next to the assembly (T-04.1).
    /// </summary>
    private static string Resolve(string path)
        => Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);

    /// <summary>
    /// A sibling <c>personality.local.yaml</c> overrides the template. That file is not versioned.
    /// </summary>
    private static string PreferLocal(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(directory))
        {
            return path;
        }

        var local = Path.Combine(directory, "personality.local.yaml");
        return File.Exists(local) ? local : path;
    }

    private static PersonalityProfile Map(PersonalityYaml yaml, string origin)
    {
        var problems = new List<string>();

        var style = yaml.Style ?? new StyleYaml();
        var formality = ParseAxis(style.Formality, "formality", FormalityLevel.Medium, problems);
        var humor = ParseAxis(style.Humor, "humor", HumorLevel.Moderate, problems);
        var verbosity = ParseAxis(style.Verbosity, "verbosity", VerbosityLevel.Balanced, problems);
        var emoji = ParseAxis(style.Emoji, "emoji", EmojiUse.Never, problems);

        if (problems.Count > 0)
        {
            throw new PersonalityProfileInvalidException(origin, problems);
        }

        return new PersonalityProfile
        {
            Name = yaml.Name?.Trim() ?? string.Empty,
            Language = string.IsNullOrWhiteSpace(yaml.Language) ? "es-AR" : yaml.Language.Trim(),
            TimeZone = string.IsNullOrWhiteSpace(yaml.Timezone) ? "UTC" : yaml.Timezone.Trim(),
            Relationship = yaml.Relationship,
            Style = new PersonalityStyle
            {
                Formality = formality,
                Humor = humor,
                Verbosity = verbosity,
                Emoji = emoji,
            },
            Traits = yaml.Traits ?? [],
            Boundaries = yaml.Boundaries ?? [],
            Instructions = yaml.Instructions,
            Character = MapCharacter(yaml.Character),
            Voice = new VoiceSettings
            {
                Provider = yaml.Voice?.Provider ?? "default",
                VoiceId = yaml.Voice?.VoiceId ?? string.Empty,
                Speed = yaml.Voice?.Speed ?? 1.0,
            },
            Avatar = new AvatarSettings
            {
                Model = yaml.Avatar?.Model ?? "avatars/default.vrm",
                IdleAnimation = yaml.Avatar?.IdleAnimation ?? "idle",
            },
        };
    }

    private static CharacterCatalog MapCharacter(CharacterYaml? yaml)
    {
        if (yaml is null)
        {
            return CharacterCatalog.Empty;
        }

        return new CharacterCatalog(
            MapEntries(yaml.Likes),
            MapEntries(yaml.Dislikes),
            MapQuirks(yaml.Quirks));
    }

    private static CharacterEntry[] MapEntries(List<CharacterEntryYaml>? entries)
    {
        if (entries is null || entries.Count == 0)
        {
            return [];
        }

        return entries
            .Select(e => new CharacterEntry(
                e.Id?.Trim() ?? string.Empty,
                e.Line?.Trim() ?? string.Empty,
                MapTags(e.Tags)))
            .ToArray();
    }

    private static QuirkEntry[] MapQuirks(List<QuirkYaml>? quirks)
    {
        if (quirks is null || quirks.Count == 0)
        {
            return [];
        }

        return quirks
            .Select(q => new QuirkEntry(
                q.Id?.Trim() ?? string.Empty,
                q.Line?.Trim() ?? string.Empty,
                q.Always ?? false,
                MapTags(q.Tags)))
            .ToArray();
    }

    private static string[] MapTags(List<string>? tags)
    {
        if (tags is null || tags.Count == 0)
        {
            return [];
        }

        return tags.Select(t => t?.Trim() ?? string.Empty).ToArray();
    }

    private static T ParseAxis<T>(string? value, string key, T defaultValue, List<string> problems)
        where T : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        if (Enum.TryParse<T>(value, ignoreCase: true, out var result))
        {
            return result;
        }

        var allowed = string.Join(", ", Enum.GetNames<T>().Select(n => n.ToLowerInvariant()));
        problems.Add($"Value '{value}' for '{key}' is invalid. Allowed values: {allowed}.");
        return defaultValue;
    }
}
