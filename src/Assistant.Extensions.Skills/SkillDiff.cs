using System.Text;

namespace Assistant.Extensions.Skills;

internal static class SkillDiff
{
    public static string Format(string name, string? current, string proposed)
    {
        var relative = $"config/skills/{name}/SKILL.md";
        var oldHeader = current is null ? "/dev/null" : "a/" + relative;
        var newHeader = "b/" + relative;
        var oldLines = current is null ? [] : SplitLines(current);
        var newLines = SplitLines(proposed);
        var edits = DiffLines(oldLines, newLines);
        return Render(oldHeader, newHeader, edits);
    }

    private static string Render(string oldHeader, string newHeader, List<Edit> edits)
    {
        var builder = new StringBuilder();
        builder.Append("--- ").Append(oldHeader).Append('\n');
        builder.Append("+++ ").Append(newHeader).Append('\n');

        const int context = 3;
        var index = 0;
        while (index < edits.Count)
        {
            while (index < edits.Count && edits[index].Kind == ' ')
            {
                index++;
            }

            if (index >= edits.Count)
            {
                break;
            }

            var hunkStart = Math.Max(0, index - context);
            var hunkEnd = index;
            while (hunkEnd < edits.Count)
            {
                while (hunkEnd < edits.Count && edits[hunkEnd].Kind != ' ')
                {
                    hunkEnd++;
                }

                var nextChange = hunkEnd;
                while (nextChange < edits.Count && edits[nextChange].Kind == ' ')
                {
                    nextChange++;
                }

                if (nextChange >= edits.Count)
                {
                    hunkEnd = Math.Min(edits.Count, hunkEnd + context);
                    break;
                }

                if (nextChange - hunkEnd <= context * 2)
                {
                    hunkEnd = nextChange;
                    continue;
                }

                hunkEnd = Math.Min(edits.Count, hunkEnd + context);
                break;
            }

            var oldStart = 1;
            var newStart = 1;
            for (var cursor = 0; cursor < hunkStart; cursor++)
            {
                if (edits[cursor].Kind != '+')
                {
                    oldStart++;
                }

                if (edits[cursor].Kind != '-')
                {
                    newStart++;
                }
            }

            var oldCount = 0;
            var newCount = 0;
            for (var cursor = hunkStart; cursor < hunkEnd; cursor++)
            {
                if (edits[cursor].Kind != '+')
                {
                    oldCount++;
                }

                if (edits[cursor].Kind != '-')
                {
                    newCount++;
                }
            }

            var oldLine = oldCount == 0 ? Math.Max(oldStart - 1, 0) : oldStart;
            var newLine = newCount == 0 ? Math.Max(newStart - 1, 0) : newStart;
            builder.Append("@@ -").Append(oldLine).Append(',').Append(oldCount)
                .Append(" +").Append(newLine).Append(',').Append(newCount)
                .Append(" @@").Append('\n');

            for (var cursor = hunkStart; cursor < hunkEnd; cursor++)
            {
                builder.Append(edits[cursor].Kind);
                builder.Append(edits[cursor].Text);
                builder.Append('\n');
            }

            index = hunkEnd;
        }

        return builder.ToString();
    }

    private static List<Edit> DiffLines(string[] oldLines, string[] newLines)
    {
        const int maxCells = 1_500_000;
        if ((long)oldLines.Length * newLines.Length > maxCells)
        {
            var coarse = new List<Edit>(oldLines.Length + newLines.Length);
            foreach (var line in oldLines)
            {
                coarse.Add(new Edit('-', line));
            }

            foreach (var line in newLines)
            {
                coarse.Add(new Edit('+', line));
            }

            return coarse;
        }

        var rows = oldLines.Length;
        var columns = newLines.Length;
        var scores = new int[rows + 1, columns + 1];
        for (var row = rows - 1; row >= 0; row--)
        {
            for (var column = columns - 1; column >= 0; column--)
            {
                scores[row, column] = oldLines[row] == newLines[column]
                    ? scores[row + 1, column + 1] + 1
                    : Math.Max(scores[row + 1, column], scores[row, column + 1]);
            }
        }

        var edits = new List<Edit>();
        var left = 0;
        var right = 0;
        while (left < rows && right < columns)
        {
            if (oldLines[left] == newLines[right])
            {
                edits.Add(new Edit(' ', oldLines[left]));
                left++;
                right++;
            }
            else if (scores[left + 1, right] >= scores[left, right + 1])
            {
                edits.Add(new Edit('-', oldLines[left]));
                left++;
            }
            else
            {
                edits.Add(new Edit('+', newLines[right]));
                right++;
            }
        }

        while (left < rows)
        {
            edits.Add(new Edit('-', oldLines[left]));
            left++;
        }

        while (right < columns)
        {
            edits.Add(new Edit('+', newLines[right]));
            right++;
        }

        return edits;
    }

    private static string[] SplitLines(string text)
    {
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        if (normalized.Length == 0)
        {
            return [];
        }

        var endsWithNewline = normalized.EndsWith('\n');
        var parts = normalized.Split('\n');
        return endsWithNewline ? parts[..^1] : parts;
    }

    private readonly record struct Edit(char Kind, string Text);
}
