using Assistant.Api.Configuration;
using Assistant.Persistence.Auth;
using Microsoft.Extensions.Options;

namespace Assistant.Api.Authentication;

internal sealed class PasswordCredentialService(
    IOptions<AuthOptions> options,
    IUserPasswordStore store,
    Argon2idPasswordHasher hasher)
{
    public async Task EnsureInitialAsync(CancellationToken cancellationToken)
    {
        var existing = await store.GetHashAsync(cancellationToken);
        if (existing is not null)
        {
            return;
        }

        var password = options.Value.InitialPassword;
        if (string.IsNullOrEmpty(password))
        {
            throw new InvalidOperationException(
                "Auth:InitialPassword is required when no password hash is stored.");
        }

        if (!PasswordRules.IsAcceptable(password))
        {
            throw new InvalidOperationException(
                "Auth:InitialPassword must be between 12 and 128 characters.");
        }

        await store.SetHashAsync(hasher.Hash(password), cancellationToken);
    }

    public async Task ReplaceAsync(CancellationToken cancellationToken)
    {
        var password = options.Value.NewPassword;
        if (string.IsNullOrEmpty(password))
        {
            throw new InvalidOperationException("Auth:NewPassword is required.");
        }

        if (!PasswordRules.IsAcceptable(password))
        {
            throw new InvalidOperationException(
                "Auth:NewPassword must be between 12 and 128 characters.");
        }

        await store.SetHashAsync(hasher.Hash(password), cancellationToken);
    }

    public async Task<bool> VerifyAsync(string? password, CancellationToken cancellationToken)
    {
        if (!PasswordRules.IsAcceptable(password))
        {
            return false;
        }

        var hash = await store.GetHashAsync(cancellationToken);
        if (string.IsNullOrEmpty(hash))
        {
            return false;
        }

        return hasher.Verify(hash, password);
    }
}
