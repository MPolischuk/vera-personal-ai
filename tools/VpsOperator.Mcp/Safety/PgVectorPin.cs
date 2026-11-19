namespace VpsOperator.Mcp;

internal static class PgVectorPin
{
    public const string Tag = "0.8.7-pg17";

    public const string Digest = "sha256:ac08538c6f8b9904c33c8224c5e5706dbe760aca29db1d096972b4052c22a75d";

    public static bool IsPinnedTag(string tag) =>
        string.Equals(tag, Tag, StringComparison.Ordinal);
}
