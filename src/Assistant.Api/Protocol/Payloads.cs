namespace Assistant.Api.Protocol;

internal sealed record UserMessagePayload(Guid ConversationId, string Text);

internal sealed record ConfirmationResponsePayload(Guid ConfirmationId, bool Approved);

internal sealed record InterruptPayload(int? PlayedChunks);

internal sealed record TranscriptionPayload(string Text, bool IsFinal);

internal sealed record TurnStartedPayload(Guid TurnId, Guid ConversationId);

internal sealed record TokenPayload(string Text);

internal sealed record ToolInvokedPayload(string ToolName);

internal sealed record ToolCompletedPayload(string ToolName, string Outcome, bool Succeeded);

internal sealed record ConfirmationRequestPayload(
    Guid ConfirmationId,
    string ToolName,
    string Prompt,
    DateTimeOffset ExpiresAt);

internal sealed record TurnCompletedPayload(
    Guid? MessageId,
    string Outcome,
    int DurationMs);

internal sealed record ErrorPayload(string Code, string Message);

internal sealed record NotificationActionPayload(string ActionId, string Label);

internal sealed record NotificationPayload(
    Guid NotificationId,
    Guid DeliveryId,
    string Kind,
    string Title,
    string Body,
    IReadOnlyList<NotificationActionPayload> Actions,
    Guid? ConversationId,
    DateTimeOffset CreatedAt);

internal sealed record NotificationAckPayload(Guid DeliveryId);

internal sealed record NotificationActionRequestPayload(Guid NotificationId, string Action);

internal sealed record AvatarStatePayload(string State);

internal sealed record AvatarExpressionPayload(string Expression, double Weight, int DurationMs);

internal sealed record AvatarGesturePayload(string Gesture, double Intensity, int DurationMs);
