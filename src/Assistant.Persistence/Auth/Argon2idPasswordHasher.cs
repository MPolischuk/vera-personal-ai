using System.Diagnostics.CodeAnalysis;
using Isopoh.Cryptography.Argon2;

namespace Assistant.Persistence.Auth;

[SuppressMessage(
    "Performance",
    "CA1822:Mark members as static",
    Justification = "Registered as a singleton so callers depend on an instance, not a static type.")]
public sealed class Argon2idPasswordHasher
{
    public string Hash(string password) => Argon2.Hash(password);

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "A malformed PHC string must not throw and must not be logged.")]
    public bool Verify(string encoded, string password)
    {
        try
        {
            return Argon2.Verify(encoded, password);
        }
        catch (Exception)
        {
            return false;
        }
    }
}
