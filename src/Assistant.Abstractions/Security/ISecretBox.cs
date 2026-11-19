namespace Assistant.Abstractions.Security;

/// <summary>
/// Protects a secret at rest. The plaintext exists only in memory for the caller.
/// </summary>
public interface ISecretBox
{
    /// <summary>Returns ciphertext. Does not log <paramref name="plaintext"/>.</summary>
    public string Protect(string plaintext);

    /// <summary>Returns plaintext. Does not log <paramref name="ciphertext"/>.</summary>
    public string Unprotect(string ciphertext);
}
