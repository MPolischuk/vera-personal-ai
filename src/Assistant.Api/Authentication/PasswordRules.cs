using System.Diagnostics.CodeAnalysis;

namespace Assistant.Api.Authentication;

internal static class PasswordRules
{
    public const int MinimumLength = 12;

    public const int MaximumLength = 128;

    public static bool IsAcceptable([NotNullWhen(true)] string? password)
        => password is { Length: >= MinimumLength and <= MaximumLength };
}
