using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SttBakeoff;

/// <summary>
/// Spoken-number expansion for Rioplatense Spanish. The same rewrite runs on the
/// reference and the hypothesis: hours, dates, quantities, and amounts become words.
/// </summary>
internal static partial class SpanishNumbers
{
    private static readonly string[] Units =
    [
        "cero", "uno", "dos", "tres", "cuatro", "cinco", "seis", "siete", "ocho", "nueve",
        "diez", "once", "doce", "trece", "catorce", "quince", "dieciséis", "diecisiete",
        "dieciocho", "diecinueve", "veinte", "veintiuno", "veintidós", "veintitrés",
        "veinticuatro", "veinticinco", "veintiséis", "veintisiete", "veintiocho", "veintinueve",
    ];

    private static readonly string[] Tens =
    [
        "", "", "", "treinta", "cuarenta", "cincuenta", "sesenta", "setenta", "ochenta", "noventa",
    ];

    private static readonly string[] Hundreds =
    [
        "", "ciento", "doscientos", "trescientos", "cuatrocientos", "quinientos",
        "seiscientos", "setecientos", "ochocientos", "novecientos",
    ];

    private static readonly string[] Months =
    [
        "", "enero", "febrero", "marzo", "abril", "mayo", "junio",
        "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre",
    ];

    public static string Expand(string lowered)
    {
        var text = CurrencyPrefix().Replace(lowered, string.Empty);
        text = text.Replace("$", string.Empty, StringComparison.Ordinal);
        text = TimePattern().Replace(text, ExpandTime);
        text = DatePattern().Replace(text, ExpandDate);
        text = PercentPattern().Replace(text, ExpandPercent);
        return NumberPattern().Replace(text, ExpandNumber);
    }

    public static string StripDiacritics(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(ch);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static string ExpandTime(Match match)
    {
        var hour = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var minute = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        if (hour > 23 || minute > 59)
        {
            return match.Value;
        }

        var hourWords = hour == 1 ? "una" : Cardinal(hour);
        if (minute == 0)
        {
            return hourWords;
        }

        if (minute == 15)
        {
            return hourWords + " y cuarto";
        }

        if (minute == 30)
        {
            return hourWords + " y media";
        }

        return hourWords + " y " + Cardinal(minute);
    }

    private static string ExpandDate(Match match)
    {
        var day = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var month = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        if (day is < 1 or > 31 || month is < 1 or > 12)
        {
            return match.Value;
        }

        var dayWords = day == 1 ? "primero" : Cardinal(day);
        var words = dayWords + " de " + Months[month];
        if (!match.Groups[3].Success)
        {
            return words;
        }

        var year = int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
        return words + " de " + Cardinal(year);
    }

    private static string ExpandPercent(Match match)
    {
        return ParseNumber(match.Groups[1].Value) + " por ciento";
    }

    private static string ExpandNumber(Match match)
    {
        return ParseNumber(match.Groups[1].Value);
    }

    private static string ParseNumber(string raw)
    {
        if (ThousandsPattern().IsMatch(raw))
        {
            return Cardinal(long.Parse(raw.Replace(".", string.Empty, StringComparison.Ordinal), CultureInfo.InvariantCulture));
        }

        var comma = raw.IndexOf(',');
        if (comma < 0)
        {
            return Cardinal(long.Parse(raw, CultureInfo.InvariantCulture));
        }

        var whole = Cardinal(long.Parse(raw[..comma], CultureInfo.InvariantCulture));
        return whole + " coma " + Fraction(raw[(comma + 1)..]);
    }

    private static string Fraction(string digits)
    {
        if (digits.Length > 1 && digits[0] == '0')
        {
            var parts = new string[digits.Length];
            for (var i = 0; i < digits.Length; i++)
            {
                parts[i] = Units[digits[i] - '0'];
            }

            return string.Join(' ', parts);
        }

        return Cardinal(long.Parse(digits, CultureInfo.InvariantCulture));
    }

    private static string Cardinal(long value)
    {
        if (value == 0)
        {
            return "cero";
        }

        var parts = new List<string>();
        if (value >= 1_000_000_000_000)
        {
            var billions = value / 1_000_000_000_000;
            parts.Add(billions == 1 ? "un billón" : Apocope(billions) + " billones");
            value %= 1_000_000_000_000;
        }

        if (value >= 1_000_000)
        {
            var millions = value / 1_000_000;
            parts.Add(millions == 1 ? "un millón" : Apocope(millions) + " millones");
            value %= 1_000_000;
        }

        if (value >= 1000)
        {
            var thousands = value / 1000;
            parts.Add(thousands == 1 ? "mil" : Apocope(thousands) + " mil");
            value %= 1000;
        }

        if (value > 0)
        {
            parts.Add(UnderThousand((int)value, apocope: false));
        }

        return string.Join(' ', parts);
    }

    private static string Apocope(long value)
    {
        if (value == 1)
        {
            return "un";
        }

        var full = Cardinal(value);
        if (full.EndsWith("veintiuno", StringComparison.Ordinal))
        {
            return full[..^"uno".Length] + "ún";
        }

        if (full.EndsWith(" uno", StringComparison.Ordinal))
        {
            return full[..^"uno".Length] + "un";
        }

        return full;
    }

    private static string UnderThousand(int value, bool apocope)
    {
        if (value == 100)
        {
            return "cien";
        }

        if (value < 100)
        {
            return UnderHundred(value, apocope);
        }

        var words = Hundreds[value / 100];
        var rest = value % 100;
        return rest == 0 ? words : words + " " + UnderHundred(rest, apocope);
    }

    private static string UnderHundred(int value, bool apocope)
    {
        if (value < 30)
        {
            if (apocope && value == 1)
            {
                return "un";
            }

            if (apocope && value == 21)
            {
                return "veintiún";
            }

            return Units[value];
        }

        var words = Tens[value / 10];
        var unit = value % 10;
        if (unit == 0)
        {
            return words;
        }

        var unitWord = apocope && unit == 1 ? "un" : Units[unit];
        return words + " y " + unitWord;
    }

    [GeneratedRegex(@"\b(?:u\$s|usd|ars)\s*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CurrencyPrefix();

    [GeneratedRegex(@"\b(\d{1,2}):(\d{2})\b", RegexOptions.CultureInvariant)]
    private static partial Regex TimePattern();

    [GeneratedRegex(@"\b(\d{1,2})/(\d{1,2})(?:/(\d{2,4}))?\b", RegexOptions.CultureInvariant)]
    private static partial Regex DatePattern();

    [GeneratedRegex(@"\b(\d{1,3}(?:\.\d{3})+|\d+(?:,\d+)?)%", RegexOptions.CultureInvariant)]
    private static partial Regex PercentPattern();

    [GeneratedRegex(@"\b(\d{1,3}(?:\.\d{3})+|\d+(?:,\d+)?)\b", RegexOptions.CultureInvariant)]
    private static partial Regex NumberPattern();

    [GeneratedRegex(@"^\d{1,3}(\.\d{3})+$", RegexOptions.CultureInvariant)]
    private static partial Regex ThousandsPattern();
}
