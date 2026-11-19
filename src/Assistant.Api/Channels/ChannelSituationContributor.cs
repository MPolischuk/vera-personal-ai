using Assistant.Abstractions.Channels;
using Assistant.Abstractions.Context;

namespace Assistant.Api.Channels;

/// <summary>
/// Short situational fragment for a remote channel. The hub contributes nothing (CN-8).
/// Same priority band as the character situational contributor, so history is trimmed first.
/// </summary>
internal sealed class ChannelSituationContributor(ITurnOriginAccessor origin) : IContextContributor
{
    public const string TelegramText =
        "Ahora te escribe por Telegram desde el celular. No hay avatar, voz en vivo ni pantalla de la app: no los menciones. Respondé breve, en texto simple (negrita o cursiva como mucho; nada de tablas, imágenes ni bloques largos; una cita va como [^id] y la definición al final). Si lo que pide necesita la app, decíselo.";

    private static readonly Dictionary<ChannelId, string> Texts = new()
        {
            [ChannelId.Telegram] = TelegramText,
        };

    public string Name => "channel.situation";

    public int Priority => 20;

    public bool IsEnabled => true;

    public ValueTask<ContextFragment?> ContributeAsync(ContextRequest request, CancellationToken ct)
    {
        if (!Texts.TryGetValue(origin.Current.Channel, out var text))
        {
            return ValueTask.FromResult<ContextFragment?>(null);
        }

        return ValueTask.FromResult<ContextFragment?>(
            ContextFragment.Text(Name, ContextPlacement.AppendedMessage, text));
    }
}
