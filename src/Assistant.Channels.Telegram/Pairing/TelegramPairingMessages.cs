using System.Globalization;
using System.Net;
using Assistant.Abstractions.Channels;
using Assistant.Channels.Telegram.Api;
using Assistant.Channels.Telegram.Api.Dto;

namespace Assistant.Channels.Telegram.Pairing;

/// <summary>Fixed pairing copy. The code is interpolated into the text and must not be logged.</summary>
public static class TelegramPairingMessages
{
    public static string Format(PairingNotice notice) => notice switch
    {
        PairingNotice.NewCode code =>
            "Hola. Todavía no te conozco. Si este código es tuyo, abrí Vera → Ajustes → Canales y aprobalo: <b>"
            + Html(code.Code)
            + "</b> (vence en una hora). Hasta entonces no puedo responderte.",
        PairingNotice.Reminder reminder =>
            "Sigo sin conocerte. El código es <b>"
            + Html(reminder.Code)
            + "</b>. Abrí Vera → Ajustes → Canales y aprobame.",
        PairingNotice.Welcome =>
            "Listo, ya podés hablarme por acá. Probá con \"recordame en 2 minutos que…\".",
        PairingNotice.LinkRejected =>
            "Ese enlace ya no sirve. Pedí uno nuevo en Vera → Ajustes → Canales.",
        _ => throw new ArgumentOutOfRangeException(nameof(notice)),
    };

    public static string DeepLink(string botUsername, string token)
        => "https://t.me/" + botUsername.Trim().TrimStart('@') + "?start=" + token;

    private static string Html(string value) => WebUtility.HtmlEncode(value);
}

/// <summary>Sends pairing notices through the Bot API. The text is not logged.</summary>
public sealed class TelegramPairingMessenger(ITelegramBotClient client) : IPairingMessenger
{
    public ChannelId Channel => ChannelId.Telegram;

    public async ValueTask SendAsync(string externalPeerId, PairingNotice notice, CancellationToken ct)
    {
        if (!long.TryParse(externalPeerId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var chatId))
        {
            return;
        }

        await client.SendMessageAsync(
            new SendMessageRequest
            {
                ChatId = chatId,
                Text = TelegramPairingMessages.Format(notice),
                ParseMode = "HTML",
            },
            ct);
    }
}
