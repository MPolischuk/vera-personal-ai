namespace Assistant.Api.Configuration;

internal enum AuthMode
{
    Bearer,
    Password,
}

internal static class AuthModes
{
    public static bool TryParse(string? raw, out AuthMode mode)
    {
        if (string.IsNullOrWhiteSpace(raw)
            || raw.Equals(nameof(AuthMode.Bearer), StringComparison.OrdinalIgnoreCase))
        {
            mode = AuthMode.Bearer;
            return true;
        }

        if (raw.Equals(nameof(AuthMode.Password), StringComparison.OrdinalIgnoreCase))
        {
            mode = AuthMode.Password;
            return true;
        }

        mode = default;
        return false;
    }
}
