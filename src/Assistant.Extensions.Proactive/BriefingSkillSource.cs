using Microsoft.Extensions.Options;

namespace Assistant.Extensions.Proactive;

/// <summary>The briefing skill text. A missing file is null and the job does not call the model.</summary>
public interface IBriefingSkillSource
{
    public ValueTask<string?> ReadAsync(CancellationToken ct);
}

/// <summary>Reads <c>config/skills/briefing/SKILL.md</c>.</summary>
public sealed class BriefingSkillFile(IOptions<BriefingOptions> options) : IBriefingSkillSource
{
    public ValueTask<string?> ReadAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var path = options.Value.SkillPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            return ValueTask.FromResult<string?>(null);
        }

        foreach (var candidate in Candidates(path))
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            try
            {
                var text = File.ReadAllText(candidate);
                return ValueTask.FromResult<string?>(string.IsNullOrWhiteSpace(text) ? null : text);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return ValueTask.FromResult<string?>(null);
            }
        }

        return ValueTask.FromResult<string?>(null);
    }

    private static IEnumerable<string> Candidates(string path)
    {
        if (Path.IsPathRooted(path))
        {
            yield return path;
            yield break;
        }

        yield return Path.Combine(AppContext.BaseDirectory, path);
        yield return Path.Combine(Directory.GetCurrentDirectory(), path);
    }
}

/// <summary>Where the briefing skill file lives.</summary>
public sealed class BriefingOptions
{
    public const string SectionName = "Briefing";

    public string SkillPath { get; set; } = Path.Combine("config", "skills", "briefing", "SKILL.md");
}
