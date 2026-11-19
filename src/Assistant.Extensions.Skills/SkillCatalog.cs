using System.Diagnostics.CodeAnalysis;
using Assistant.Abstractions.Skills;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Assistant.Extensions.Skills;

internal sealed class SkillCatalog(
    IOptions<SkillOptions> options,
    ILogger<SkillCatalog> logger,
    IConnectedMcpServers? servers = null)
{
    public IReadOnlyList<ParsedSkill> ListAvailable(IReadOnlyCollection<string>? toolNames)
        => ReadValid().Where(skill => RequirementsMet(skill, toolNames)).ToArray();

    public bool TryLoad(
        string name,
        IReadOnlyCollection<string>? toolNames,
        [NotNullWhen(true)] out ParsedSkill? skill)
    {
        foreach (var candidate in ListAvailable(toolNames))
        {
            if (string.Equals(candidate.Key, name, StringComparison.Ordinal))
            {
                skill = candidate;
                return true;
            }
        }

        skill = null;
        return false;
    }

    private bool RequirementsMet(ParsedSkill skill, IReadOnlyCollection<string>? toolNames)
    {
        foreach (var tool in skill.RequiredTools)
        {
            if (toolNames is null || !ContainsOrdinal(toolNames, tool))
            {
                return false;
            }
        }

        foreach (var server in skill.RequiredServers)
        {
            if (servers is null || !servers.IsConnected(server))
            {
                return false;
            }
        }

        return true;
    }

    private List<ParsedSkill> ReadValid()
    {
        var root = ResolveDirectory();
        if (!Directory.Exists(root))
        {
            return [];
        }

        IEnumerable<string> directories;
        try
        {
            directories = Directory.EnumerateDirectories(root);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            SkillLog.OmittingUnreadableSkill(logger, exception, root);
            return [];
        }

        var skills = new List<ParsedSkill>();
        foreach (var directory in directories)
        {
            var directoryName = Path.GetFileName(directory);
            var file = Path.Combine(directory, "SKILL.md");
            if (!File.Exists(file))
            {
                continue;
            }

            ParsedSkill? parsed;
            try
            {
                parsed = TryParse(directoryName, directory, File.ReadAllText(file));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                SkillLog.OmittingUnreadableSkill(logger, exception, directoryName);
                continue;
            }

            if (parsed is null)
            {
                continue;
            }

            skills.Add(parsed);
        }

        return skills;
    }

    private ParsedSkill? TryParse(string directoryName, string directory, string text)
    {
        if (!SkillParser.TryParse(directoryName, text, out var parsed, out var reason))
        {
            SkillLog.OmittingSkill(logger, directoryName, reason);
            return null;
        }

        return new ParsedSkill(
            parsed.Key,
            parsed.Description,
            parsed.Body,
            directory,
            parsed.RequiredTools,
            parsed.RequiredServers);
    }

    private string ResolveDirectory() => options.Value.ResolveDirectoryPath();

    private static bool ContainsOrdinal(IReadOnlyCollection<string> names, string name)
    {
        foreach (var candidate in names)
        {
            if (string.Equals(candidate, name, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    internal sealed record ParsedSkill(
        string Key,
        string Description,
        string Body,
        string DirectoryPath,
        string[] RequiredTools,
        string[] RequiredServers);
}
