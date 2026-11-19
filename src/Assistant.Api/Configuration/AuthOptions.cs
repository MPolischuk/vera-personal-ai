namespace Assistant.Api.Configuration;

internal sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public const int BearerTokenMinimumLength = 32;

    public AuthMode Mode { get; set; } = AuthMode.Bearer;

    public string BearerToken { get; set; } = string.Empty;

    public string? InitialPassword { get; set; }

    public string? NewPassword { get; set; }

    public string? JwtSigningKey { get; set; }
}
