using System.Text;
using Assistant.Abstractions.Proactive;

namespace Assistant.Extensions.Proactive;

/// <summary>Renders a stored briefing, or the fixed sentence when today's row is missing.</summary>
public static class BriefingText
{
    public const string NotReady = "Todavía no armé el briefing de hoy.";

    public static string Render(Briefing? briefing)
    {
        if (briefing is null)
        {
            return NotReady;
        }

        var builder = new StringBuilder();
        builder.AppendLine(briefing.Summary);
        AppendBlock(builder, "Hoy", briefing.Items.Where(item => item.Block == BriefingBlocks.Today));
        AppendBlock(builder, "Más adelante", briefing.Items.Where(item => item.Block == BriefingBlocks.Ahead));
        return builder.ToString().TrimEnd();
    }

    private static void AppendBlock(StringBuilder builder, string heading, IEnumerable<BriefingItem> items)
    {
        var list = items.ToArray();
        builder.AppendLine();
        builder.Append(heading);
        if (list.Length == 0)
        {
            builder.AppendLine(": nada.");
            return;
        }

        builder.AppendLine(":");
        foreach (var item in list)
        {
            builder.Append("- ");
            builder.Append(item.Title);
            builder.Append(" (");
            builder.Append(item.Triage);
            builder.Append("): ");
            builder.AppendLine(item.Reason);
        }
    }
}
