namespace Assistant.Channels.Telegram.Outbound;

/// <summary>Fixed Spanish copy for Telegram. Never includes message text from the user.</summary>
internal static class TelegramReplies
{
    public const string ModelUnavailable = "Ahora no puedo pensar, probá en un rato";

    public const string SttFailed = "No te entendí el audio, ¿me lo escribís?";

    public const string TurnInProgress = "Esperá que termino lo anterior";

    public const string VoiceTooLong = "Es muy largo; mandame algo de menos de dos minutos";

    public const string TextOnly = "Por ahora sólo leo texto por acá";

    public const string VoiceOrText = "Mandame una nota de voz o texto";

    public const string NoPhotos = "Todavía no puedo ver fotos por acá; mandame texto o un audio";

    public const string FileTooLarge = "Ese audio es demasiado pesado; mandame uno más corto.";

    public const string ConfirmFirst = "Primero confirmá o cancelá esto.";

    public const string NoLongerCurrent = "Esto ya no está vigente";

    public const string AlreadyResolved = "Ya lo resolviste";

    public const string ConfirmationExpired = "No ejecuté la acción: la confirmación venció.";

    public const string ConfirmationDenied = "No ejecuté la acción porque rechazaste la confirmación.";

    public const string NewConversation = "Arrancamos de cero. Lo que sé de vos sigue igual.";

    public const string NoReminders = "No tenés recordatorios activos.";

    public const string Start = "Hola. Ya estamos vinculados. Escribime o mandame una nota de voz.";

    public const string Help =
        "Por acá podés escribirme o mandarme una nota de voz. /nueva empieza otra conversación. /recordatorios lista los que tenés activos. Fotos, archivos y que te responda con voz se hacen desde la app.";

    public static string? ForTurnError(string? code) => code switch
    {
        "model_unavailable" => ModelUnavailable,
        "stt_failed" => SttFailed,
        "turn_in_progress" => TurnInProgress,
        "confirmation_pending" => ConfirmFirst,
        _ => null,
    };

    public static string TranscriptionEcho(string text)
        => "🎙 <i>«" + TelegramMarkdownToHtml.Escape(text) + "»</i>";
}
