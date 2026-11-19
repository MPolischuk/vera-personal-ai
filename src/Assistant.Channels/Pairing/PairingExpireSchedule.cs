using System.Text.Json;
using System.Text.Json.Serialization;

namespace Assistant.Channels.Pairing;

internal static class PairingExpireSchedule
{
    public const string Kind = "channel.pairing.expire";

    public static string Key(Guid peerId, DateTimeOffset expiresAt)
        => $"{Kind}:{peerId:N}:{expiresAt.UtcTicks}";

    public static string Payload(Guid peerId)
        => JsonSerializer.Serialize(new Body(peerId));

    public static bool TryRead(string json, out Guid peerId)
    {
        peerId = Guid.Empty;
        try
        {
            var payload = JsonSerializer.Deserialize<Body>(json);
            if (payload is null || payload.PeerId == Guid.Empty)
            {
                return false;
            }

            peerId = payload.PeerId;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private sealed record Body([property: JsonPropertyName("peerId")] Guid PeerId);
}
