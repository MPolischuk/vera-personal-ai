using System.Text;
using Microsoft.Extensions.Options;

namespace Assistant.Api.Configuration;

internal sealed class AuthOptionsValidator(IConfiguration configuration) : IValidateOptions<AuthOptions>
{
    public const int JwtSigningKeyMinimumUtf8Bytes = 32;

    public ValidateOptionsResult Validate(string? name, AuthOptions options)
    {
        if (!AuthModes.TryParse(configuration[$"{AuthOptions.SectionName}:Mode"], out var mode))
        {
            return ValidateOptionsResult.Fail("Auth:Mode must be Bearer or Password.");
        }

        if (mode == AuthMode.Bearer
            && (string.IsNullOrEmpty(options.BearerToken)
                || options.BearerToken.Length < AuthOptions.BearerTokenMinimumLength))
        {
            return ValidateOptionsResult.Fail(
                "Auth:BearerToken is required and must be at least 32 characters.");
        }

        if (mode == AuthMode.Password)
        {
            if (string.IsNullOrEmpty(options.JwtSigningKey)
                || Encoding.UTF8.GetByteCount(options.JwtSigningKey) < JwtSigningKeyMinimumUtf8Bytes)
            {
                return ValidateOptionsResult.Fail(
                    "Auth:JwtSigningKey is required in password mode and must be at least 32 UTF-8 bytes.");
            }

            if (!string.IsNullOrEmpty(options.BearerToken)
                && string.Equals(options.BearerToken, options.JwtSigningKey, StringComparison.Ordinal))
            {
                return ValidateOptionsResult.Fail(
                    "Auth:JwtSigningKey must differ from Auth:BearerToken.");
            }
        }

        return ValidateOptionsResult.Success;
    }
}
