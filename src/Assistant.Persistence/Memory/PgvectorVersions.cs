namespace Assistant.Persistence.Memory;

/// <summary>
/// Compares <c>pg_extension.extversion</c> for vector against 0.8.7.
/// A shorter prefix loses: 0.8 is older than 0.8.7. Numeric parts, so 0.8.10 is newer.
/// </summary>
internal static class PgvectorVersions
{
    public const string Minimum = "0.8.7";

    public static bool IsSupported(string? extversion)
    {
        if (!TryParse(extversion, out var parts))
        {
            return false;
        }

        int[] minimum = [0, 8, 7];
        var length = Math.Max(parts.Length, minimum.Length);
        for (var i = 0; i < length; i++)
        {
            var left = i < parts.Length ? parts[i] : 0;
            var right = i < minimum.Length ? minimum[i] : 0;
            if (left != right)
            {
                return left > right;
            }
        }

        return true;
    }

    public static void EnsureSupported(string? extversion)
    {
        if (IsSupported(extversion))
        {
            return;
        }

        throw new InvalidOperationException(
            $"pgvector {extversion ?? "missing"} is older than {Minimum}. The HNSW index was not created.");
    }

    private static bool TryParse(string? extversion, out int[] parts)
    {
        parts = [];
        if (string.IsNullOrWhiteSpace(extversion))
        {
            return false;
        }

        var parsed = new List<int>();
        foreach (var piece in extversion.Split('.'))
        {
            var digits = 0;
            var seen = false;
            foreach (var character in piece)
            {
                if (character is < '0' or > '9')
                {
                    break;
                }

                seen = true;
                digits = (digits * 10) + (character - '0');
            }

            if (!seen)
            {
                break;
            }

            parsed.Add(digits);
        }

        if (parsed.Count == 0)
        {
            return false;
        }

        parts = parsed.ToArray();
        return true;
    }
}
