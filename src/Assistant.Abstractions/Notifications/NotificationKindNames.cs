namespace Assistant.Abstractions.Notifications;

/// <summary>Wire and storage names for <see cref="NotificationKind"/>.</summary>
public static class NotificationKindNames
{
    public static string ToWire(NotificationKind kind)
        => kind switch
        {
            NotificationKind.Reminder => "reminder",
            NotificationKind.TaskFinished => "task_finished",
            NotificationKind.Proactive => "proactive",
            NotificationKind.ExternalDigest => "external_digest",
            NotificationKind.ConfirmationRequest => "confirmation_request",
            NotificationKind.System => "system",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown notification kind."),
        };

    public static NotificationKind Parse(string value)
        => value switch
        {
            "reminder" => NotificationKind.Reminder,
            "task_finished" or "taskfinished" => NotificationKind.TaskFinished,
            "proactive" => NotificationKind.Proactive,
            "external_digest" or "externaldigest" => NotificationKind.ExternalDigest,
            "confirmation_request" or "confirmationrequest" => NotificationKind.ConfirmationRequest,
            "system" => NotificationKind.System,
            _ => throw new InvalidOperationException($"Unknown notification kind '{value}'."),
        };
}
