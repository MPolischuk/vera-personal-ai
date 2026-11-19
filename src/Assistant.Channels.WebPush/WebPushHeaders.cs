using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Assistant.Abstractions.Notifications;

namespace Assistant.Channels.WebPush;

/// <summary>RFC 8030 headers for one notification kind. <see cref="Topic"/> is 22 base64url characters.</summary>
/// <param name="TtlSeconds">TTL in seconds.</param>
/// <param name="Urgency"><c>high</c>, <c>normal</c>, or <c>low</c>.</param>
/// <param name="Topic">Collapse topic, or null when this kind does not set one.</param>
internal readonly record struct WebPushDispatchHeaders(int TtlSeconds, string Urgency, string? Topic)
{
    public static WebPushDispatchHeaders For(NotificationKind kind, string? topicSource)
        => kind switch
        {
            NotificationKind.Reminder => new(3_600, "high", TopicOf(topicSource)),
            NotificationKind.TaskFinished => new(21_600, "normal", TopicOf(topicSource)),
            NotificationKind.Proactive or NotificationKind.ExternalDigest => new(43_200, "low", null),
            _ => new(86_400, "normal", null),
        };

    private static string? TopicOf(string? source)
    {
        if (string.IsNullOrEmpty(source))
        {
            return null;
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(source));
        return Base64Url.EncodeToString(hash.AsSpan(0, 16));
    }
}
