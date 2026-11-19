using Assistant.Abstractions.Security;
using Microsoft.AspNetCore.DataProtection;

namespace Assistant.Api.Security;

/// <summary>Protects MCP refresh tokens with the purpose <c>Assistant.Mcp.OAuth</c>.</summary>
internal sealed class DataProtectionSecretBox(IDataProtectionProvider provider) : ISecretBox
{
    public const string Purpose = "Assistant.Mcp.OAuth";

    private readonly IDataProtector _protector = provider.CreateProtector(Purpose);

    public string Protect(string plaintext)
    {
        ArgumentException.ThrowIfNullOrEmpty(plaintext);
        return _protector.Protect(plaintext);
    }

    public string Unprotect(string ciphertext)
    {
        ArgumentException.ThrowIfNullOrEmpty(ciphertext);
        return _protector.Unprotect(ciphertext);
    }
}
