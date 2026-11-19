using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.RegularExpressions;
using YamlDotNet.Serialization;

namespace Assistant.Extensions.Skills;

internal static partial class SkillParser
{
    public const int MaxBytes = 65_536;

    private static readonly Regex IsValidName = NamePattern();

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .IgnoreUnmatchedProperties()
        .Build();

    public static bool IsAcceptableName([NotNullWhen(true)] string? name)
        => !string.IsNullOrEmpty(name)
           && name.Length <= 64
           && IsValidName.IsMatch(name);

    public static bool IsAcceptableRequirement(string item)
        => RequirementPattern().IsMatch(item);

    public static bool TryParse(
        string directoryName,
        string text,
        [NotNullWhen(true)] out ParsedSkillText? skill,
        [NotNullWhen(false)] out string? reason)
    {
        skill = null;
        reason = null;

        if (!TrySplitFrontmatter(text, out var yaml, out var body))
        {
            reason = "frontmatter is missing";
            return false;
        }

        SkillDocument? document;
        try
        {
            document = Deserializer.Deserialize<SkillDocument>(yaml);
        }
        catch (Exception)
        {
            reason = "metadata is not a string map or the frontmatter is invalid";
            return false;
        }

        if (document is null || !IsAcceptableName(document.Name))
        {
            reason = "name is invalid";
            return false;
        }

        if (!string.Equals(document.Name, directoryName, StringComparison.Ordinal))
        {
            reason = "name does not match the directory";
            return false;
        }

        if (string.IsNullOrEmpty(document.Description) || document.Description.Length > 1024)
        {
            reason = "description length is out of range";
            return false;
        }

        skill = new ParsedSkillText(
            document.Name,
            document.Description,
            body,
            SplitList(document.Metadata, "assistant.requires.tools"),
            SplitList(document.Metadata, "assistant.requires.servers"));
        return true;
    }

    private static bool TrySplitFrontmatter(string text, out string yaml, out string body)
    {
        yaml = string.Empty;
        body = string.Empty;

        if (text.StartsWith('\uFEFF'))
        {
            text = text[1..];
        }

        using var reader = new StringReader(text);
        if (reader.ReadLine() is not "---")
        {
            return false;
        }

        var yamlBuilder = new StringBuilder();
        var closed = false;
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line is "---")
            {
                closed = true;
                break;
            }

            yamlBuilder.AppendLine(line);
        }

        if (!closed)
        {
            return false;
        }

        yaml = yamlBuilder.ToString();
        body = reader.ReadToEnd().Trim();
        return true;
    }

    private static string[] SplitList(Dictionary<string, string>? metadata, string key)
    {
        if (metadata is null
            || !metadata.TryGetValue(key, out var value)
            || string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();

    [GeneratedRegex("^[a-zA-Z0-9_.:-]{1,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex RequirementPattern();

    private sealed class SkillDocument
    {
        // Public so YamlDotNet can construct the nested type. The implicit constructor would be private.
        public SkillDocument()
        {
        }

        [YamlMember(Alias = "name")]
        public string? Name { get; set; }

        [YamlMember(Alias = "description")]
        public string? Description { get; set; }

        [YamlMember(Alias = "metadata")]
        public Dictionary<string, string>? Metadata { get; set; }
    }
}

internal sealed record ParsedSkillText(
    string Key,
    string Description,
    string Body,
    string[] RequiredTools,
    string[] RequiredServers);
