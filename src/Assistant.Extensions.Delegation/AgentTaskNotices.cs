using System.Globalization;
using Assistant.Abstractions.Notifications;
using Assistant.Abstractions.Tasks;

namespace Assistant.Extensions.Delegation;

/// <summary>Fixed finish notices. They never include the instruction or the result.</summary>
internal static class AgentTaskNotices
{
    public const int TitleMaxCharacters = 60;

    public const int BodyMaxCharacters = 1000;

    public static NotificationRequest Create(ArmedAgentTaskNotice notice)
    {
        var title = Trim(notice.Title, TitleMaxCharacters);
        var body = Trim(Body(notice), BodyMaxCharacters);
        return new NotificationRequest(
            notice.UserId,
            NotificationKind.TaskFinished,
            $"task:{notice.Id}:finished",
            title,
            body,
            ConversationId: notice.ConversationId,
            SourceType: "task",
            SourceId: notice.Id.ToString("D"),
            Trust: NotificationContentTrust.Trusted);
    }

    private static string Body(ArmedAgentTaskNotice notice)
    {
        var cost = notice.CostUsd.ToString("0.000000", CultureInfo.InvariantCulture);
        var title = notice.Title;
        return notice.State switch
        {
            AgentTaskStatuses.Completed => $"Terminé «{title}». Costo USD {cost}.",
            AgentTaskStatuses.Canceled => $"Cancelé «{title}».",
            AgentTaskStatuses.Rejected => $"No arranqué «{title}»: la delegación está apagada.",
            AgentTaskStatuses.Failed when notice.FailureReason == AgentTaskFailureReasons.Interrupted
                => "Se cortó por un reinicio. Quedó un resultado parcial. ¿La retomo?",
            AgentTaskStatuses.Failed when notice.FailureReason == AgentTaskFailureReasons.ProviderBudget
                => "Llegué al tope diario de OpenRouter. No lo reintento.",
            AgentTaskStatuses.Failed when notice.FailureReason == AgentTaskFailureReasons.Deadline
                => $"Llegó al tope de tiempo. Costo USD {cost}.",
            AgentTaskStatuses.Failed when notice.FailureReason == AgentTaskFailureReasons.CostCap
                => "Llegó al tope de costo antes de llamar al modelo.",
            _ => $"No pude terminar «{title}».",
        };
    }

    private static string Trim(string value, int max)
        => value.Length <= max ? value : value[..max];
}
