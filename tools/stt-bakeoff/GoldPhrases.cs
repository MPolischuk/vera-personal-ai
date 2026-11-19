namespace SttBakeoff;

/// <summary>
/// Reference lines for the eight-phrase gold sample. Written beside the WAV files, never committed.
/// </summary>
internal static class GoldPhrases
{
    public static readonly string[] Lines =
    [
        "Che, fijate si mañana llueve en Buenos Aires.",
        "Acordame llamar a Vera a las cinco.",
        "No, dejá, después lo vemos.",
        "¿Me buscás el mail de Alicia?",
        "Pará, eso no era lo que te pedí.",
        "Poné un recordatorio para el lunes a la mañana.",
        "¿Cómo venía el tema de Coolify?",
        "Dale, cerrá la ventana del chat.",
    ];

    /// <summary>With-context pass. Exactly the entities named in the plan, not the live app list.</summary>
    public static readonly string[] Keyterms = ["Buenos Aires", "Vera", "Alicia", "Coolify"];

    public static string ExpectedFileText()
    {
        return string.Join('\n', Lines) + "\n";
    }
}
