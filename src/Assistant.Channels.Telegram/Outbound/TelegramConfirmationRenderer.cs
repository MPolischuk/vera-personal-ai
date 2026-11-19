using Assistant.Abstractions.Channels;
using Assistant.Channels.Telegram.Api.Dto;

namespace Assistant.Channels.Telegram.Outbound;

/// <summary>Inline keyboards for confirmation cards and notification actions.</summary>
internal static class TelegramConfirmationRenderer
{
    public static InlineKeyboardMarkup ConfirmationButtons(Guid confirmationId)
        => Row(
            new InlineKeyboardButton
            {
                Text = "Confirmar",
                CallbackData = CallbackData.EncodeConfirmation(confirmationId, approved: true),
            },
            new InlineKeyboardButton
            {
                Text = "Cancelar",
                CallbackData = CallbackData.EncodeConfirmation(confirmationId, approved: false),
            });

    public static InlineKeyboardMarkup? NotificationButtons(
        Guid notificationId, IReadOnlyList<OutboundAction> actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        if (actions.Count == 0 || actions.Count > TelegramCapabilities.Value.MaxButtons)
        {
            return null;
        }

        var row = new InlineKeyboardButton[actions.Count];
        for (var index = 0; index < actions.Count; index++)
        {
            var action = actions[index];
            var data = CallbackData.EncodeNotification(notificationId, action.ActionId);
            if (data is null)
            {
                return null;
            }

            var label = action.Label;
            var max = TelegramCapabilities.Value.MaxButtonLabelLength;
            if (label.Length > max)
            {
                label = label[..max];
            }

            row[index] = new InlineKeyboardButton { Text = label, CallbackData = data };
        }

        return Row(row);
    }

    private static InlineKeyboardMarkup Row(params InlineKeyboardButton[] buttons)
        => new() { InlineKeyboard = [buttons] };
}
