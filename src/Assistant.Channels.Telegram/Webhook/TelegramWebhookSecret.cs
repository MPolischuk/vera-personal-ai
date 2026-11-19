using System.Security.Cryptography;
using System.Text;

namespace Assistant.Channels.Telegram.Webhook;

internal static class TelegramWebhookSecret
{
    public static bool Matches(string expected, string provided)
    {
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var providedBytes = Encoding.UTF8.GetBytes(provided);
        if (expectedBytes.Length != providedBytes.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes);
    }
}
