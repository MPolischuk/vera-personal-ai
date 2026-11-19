using System.Globalization;

namespace Assistant.Core.Personality;

internal static class PersonalityPromptComposer
{
    /// <summary>
    /// Stable instruction when the catalog has likes, dislikes, or situational quirks (D1/D3).
    /// Two lines joined with \n; design §2.3.
    /// </summary>
    private const string CharacterRestraintInstruction =
        "Tenés gustos y límites propios. No los enumerés. Si en el contexto del turno\n" +
        "aparece algo que te toca, que se note; si no, no lo menciones.";

    /// <summary>
    /// Stable cue preamble instruction (PT-24 D4; wording tightened after live 8B ignored it).
    /// Always present; before boundaries. Design §5.2: adjusting copy is content, not protocol.
    /// </summary>
    internal const string CharacterCueInstruction =
        "Siempre, en todas las respuestas (también un saludo de tres palabras), la primera línea es exactamente [[cue:expresión]] o [[cue:expresión|gesto]] y un salto de línea. Nada más en esa línea. Después, el texto. No la leas ni la expliques. No uses <wave>, <shrug>, <surprised> ni ningún otro tag: sólo [[cue:…]].\n" +
        "No copies ejemplos. No escribas la palabra Dale. Después de una acción no repitas [[cue:…]]; esa línea va una sola vez al abrir el turno.\n" +
        "expresión: happy = alegría; angry = enojo; sad = tristeza; relaxed = calma; surprised = sorpresa; neutral = sin carga. Nunca omitas esa línea: si no hay carga usá [[cue:neutral]] o [[cue:none]].\n" +
        "gesto: nod = sí/acuerdo; shake = no/rechazo; wave = saludo; shrug = no sé/da igual. Si no hay gesto, omitilo.";

    public static string Compose(PersonalityProfile profile)
    {
        var lines = new List<string> { $"Sos {profile.Name}." };

        if (!string.IsNullOrWhiteSpace(profile.Relationship))
        {
            lines.Add(string.Empty);
            lines.Add(profile.Relationship.Trim());
        }

        lines.Add(string.Empty);
        lines.Add("Cómo hablás:");
        lines.Add($"- {Formality(profile.Style.Formality)}");
        lines.Add($"- {Humor(profile.Style.Humor)}");
        lines.Add($"- {Verbosity(profile.Style.Verbosity)}");
        lines.Add($"- {Emojis(profile.Style.Emoji)}");
        lines.Add($"- Escribís en {Language(profile.Language)}.");

        AddSection(lines, "Cómo sos:", profile.Traits);

        var catalog = profile.Character;
        var alwaysLines = catalog.Quirks
            .Where(q => q.Always)
            .Select(q => q.Line)
            .ToArray();
        AddSection(lines, "Manías:", alwaysLines);

        var hasSituationalCatalog =
            catalog.Likes.Count > 0
            || catalog.Dislikes.Count > 0
            || catalog.Quirks.Any(q => !q.Always);
        if (hasSituationalCatalog)
        {
            lines.Add(string.Empty);
            lines.Add(CharacterRestraintInstruction);
        }

        AddSection(lines, "Lo que no hacés:", profile.Boundaries);

        if (!string.IsNullOrWhiteSpace(profile.Instructions))
        {
            lines.Add(string.Empty);
            lines.Add(profile.Instructions.TrimEnd());
        }

        // Last on purpose: live Gemini/Llama bury a mid-prompt cue under YAML instructions.
        lines.Add(string.Empty);
        lines.Add(CharacterCueInstruction);

        // D6: never AppendLine. The same profile must yield the same text on Windows and Linux.
        return string.Join("\n", lines);
    }

    /// <summary>An empty section does not print its header: a title followed by nothing is noise.</summary>
    private static void AddSection(List<string> lines, string title, IReadOnlyList<string> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        lines.Add(string.Empty);
        lines.Add(title);
        lines.AddRange(items.Select(item => $"- {item.Trim()}"));
    }

    private static string Language(string culture)
        => CultureInfo.GetCultureInfo(culture).NativeName;

    private static string Formality(FormalityLevel level) => level switch
    {
        FormalityLevel.Low =>
            "Tuteás y hablás informal, como alguien de confianza.",
        FormalityLevel.Medium =>
            "Mantenés un tono cercano pero respetuoso: ni demasiado coloquial ni formal.",
        FormalityLevel.High =>
            "Usás un trato formal y cuidadoso. Evitás el tuteo y las expresiones demasiado casuales.",
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, null),
    };

    private static string Humor(HumorLevel level) => level switch
    {
        HumorLevel.None =>
            "No usás humor: respondés en serio, sin bromas ni ironía.",
        HumorLevel.Moderate =>
            "Usás humor cuando viene al caso, sin forzarlo.",
        HumorLevel.High =>
            "Incluís humor con naturalidad cuando aporta, sin que opaque la respuesta útil.",
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, null),
    };

    private static string Verbosity(VerbosityLevel level) => level switch
    {
        VerbosityLevel.Concise =>
            "Respondés en pocas oraciones. No repetís la pregunta ni resumís lo que acabás de decir.",
        VerbosityLevel.Balanced =>
            "Ajustás el largo a la pregunta: breve para lo simple, desarrollado cuando hace falta.",
        VerbosityLevel.Detailed =>
            "Desarrollás las respuestas y explicás el razonamiento cuando aporta.",
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, null),
    };

    private static string Emojis(EmojiUse level) => level switch
    {
        EmojiUse.Never =>
            "No usás emojis.",
        EmojiUse.Occasional =>
            "Podés usar un emoji de vez en cuando, sólo si suma claridad o tono.",
        EmojiUse.Frequent =>
            "Usás emojis con naturalidad para reforzar el tono, sin saturar el texto.",
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, null),
    };
}
