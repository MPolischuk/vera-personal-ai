namespace Assistant.Core.Speech;

/// <summary>Closed es-AR abbreviation set, without the trailing sentence period.</summary>
internal static class SpanishSentenceAbbreviations
{
    internal static readonly HashSet<string> Values = new(StringComparer.OrdinalIgnoreCase)
    {
        "Sr",
        "Sra",
        "Srta",
        "Dr",
        "Dra",
        "Prof",
        "Profa",
        "Lic",
        "Ing",
        "Arq",
        "Ud",
        "Uds",
        "etc",
        "vs",
        "p.ej",
        "p. ej",
        "ej",
        "n.º",
        "nro",
        "a.m",
        "p.m",
        "EE.UU",
        "EE. UU",
        "Bs.As",
        "Bs. As",
    };
}
