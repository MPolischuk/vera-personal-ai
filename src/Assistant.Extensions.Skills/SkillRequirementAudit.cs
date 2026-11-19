namespace Assistant.Extensions.Skills;

public static class SkillRequirementAudit
{
    public static IReadOnlyList<SkillRequirementGap> FindGaps(
        string skillsDirectory,
        IReadOnlySet<string> toolNames,
        IReadOnlySet<string> serverIds)
    {
        ArgumentNullException.ThrowIfNull(skillsDirectory);
        ArgumentNullException.ThrowIfNull(toolNames);
        ArgumentNullException.ThrowIfNull(serverIds);

        if (!Directory.Exists(skillsDirectory))
        {
            return [];
        }

        var tools = new HashSet<string>(toolNames, StringComparer.Ordinal);
        var servers = new HashSet<string>(serverIds, StringComparer.Ordinal);
        IEnumerable<string> directories;
        try
        {
            directories = Directory.EnumerateDirectories(skillsDirectory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        var gaps = new List<SkillRequirementGap>();
        foreach (var directory in directories)
        {
            var directoryName = Path.GetFileName(directory);
            var file = Path.Combine(directory, "SKILL.md");
            if (!File.Exists(file))
            {
                continue;
            }

            string text;
            try
            {
                text = File.ReadAllText(file);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            if (!SkillParser.TryParse(directoryName, text, out var skill, out _))
            {
                continue;
            }

            foreach (var tool in skill.RequiredTools)
            {
                if (!tools.Contains(tool))
                {
                    gaps.Add(new SkillRequirementGap($"{skill.Key} tool {tool}"));
                }
            }

            foreach (var server in skill.RequiredServers)
            {
                if (!servers.Contains(server))
                {
                    gaps.Add(new SkillRequirementGap($"{skill.Key} server {server}"));
                }
            }
        }

        return gaps;
    }
}

public readonly record struct SkillRequirementGap(string Subject);
