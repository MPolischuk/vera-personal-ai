namespace Assistant.Extensions.Skills;

public sealed class SkillOptions
{
    public const string SectionName = "Skills";

    public const string DefaultDirectory = "config/skills";

    public string DirectoryPath { get; set; } = DefaultDirectory;

    public string ResolveDirectoryPath()
    {
        var path = DirectoryPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            path = DefaultDirectory;
        }

        return Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);
    }
}
