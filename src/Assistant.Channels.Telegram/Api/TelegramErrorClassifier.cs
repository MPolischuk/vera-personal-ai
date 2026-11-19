using Assistant.Abstractions.Channels;

namespace Assistant.Channels.Telegram.Api;

/// <summary>Maps a Bot API error onto <see cref="OutboundResult"/> using the stage error table.</summary>
public static class TelegramErrorClassifier
{
    public static OutboundResult Classify(TelegramApiException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return Classify(exception.ErrorCode, exception.Description, exception.RetryAfter);
    }

    public static OutboundResult Classify(int? errorCode, string? description, TimeSpan? retryAfter)
    {
        var text = description ?? "";

        if (errorCode == 429)
        {
            return new OutboundResult.TransientFailure("rate_limit", retryAfter);
        }

        if (errorCode == 403 && text.Contains("bot was blocked", StringComparison.OrdinalIgnoreCase))
        {
            return new OutboundResult.PermanentFailure("403_blocked", DeactivateTarget: true);
        }

        if (errorCode == 403)
        {
            return new OutboundResult.PermanentFailure("forbidden", DeactivateTarget: false);
        }

        if (errorCode == 400 && text.Contains("parse entities", StringComparison.OrdinalIgnoreCase))
        {
            return new OutboundResult.TransientFailure("cant_parse_entities", null);
        }

        if (errorCode == 400 && text.Contains("chat not found", StringComparison.OrdinalIgnoreCase))
        {
            return new OutboundResult.PermanentFailure("chat_not_found", DeactivateTarget: true);
        }

        if (errorCode == 400 && text.Contains("message is not modified", StringComparison.OrdinalIgnoreCase))
        {
            return new OutboundResult.NotApplicable("message_not_modified");
        }

        if (errorCode == 401)
        {
            return new OutboundResult.PermanentFailure("unauthorized", DeactivateTarget: false);
        }

        if (errorCode is null or >= 500)
        {
            return new OutboundResult.TransientFailure("unavailable", null);
        }

        return new OutboundResult.PermanentFailure("rejected", DeactivateTarget: false);
    }
}
