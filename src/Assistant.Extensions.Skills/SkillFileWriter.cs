using System.Text;
using Microsoft.Extensions.Options;

namespace Assistant.Extensions.Skills;

internal sealed class SkillFileWriter(IOptions<SkillOptions> options)
{
    public string? ReadCurrent(string name)
    {
        if (!SkillParser.IsAcceptableName(name) || SkillFilePath(name) is not string path || !File.Exists(path))
        {
            return null;
        }

        return File.ReadAllText(path);
    }

    public async ValueTask WriteAsync(string name, string content, CancellationToken ct)
    {
        if (!SkillParser.TryParse(name, content, out _, out _))
        {
            throw new InvalidOperationException("The skill text is invalid.");
        }

        if (SkillFilePath(name) is not string path)
        {
            throw new InvalidOperationException("The skill path escapes the skills directory.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), ct);
        File.Move(temporary, path, overwrite: true);
    }

    private string? SkillFilePath(string name)
    {
        var root = Path.GetFullPath(options.Value.ResolveDirectoryPath());
        var directory = Path.GetFullPath(Path.Combine(root, name));
        var prefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!directory.StartsWith(prefix, comparison))
        {
            return null;
        }

        return Path.Combine(directory, "SKILL.md");
    }
}
