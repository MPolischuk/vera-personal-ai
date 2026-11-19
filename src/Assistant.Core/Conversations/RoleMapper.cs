using Assistant.Abstractions;
using Microsoft.Extensions.AI;

namespace Assistant.Core.Conversations;

internal static class RoleMapper
{
    public static ChatRole ToChatRole(MessageRole role) => role switch
    {
        MessageRole.User => ChatRole.User,
        MessageRole.Assistant => ChatRole.Assistant,
        MessageRole.System => ChatRole.System,
        MessageRole.Tool => ChatRole.Tool,
        _ => throw new ArgumentOutOfRangeException(nameof(role)),
    };
}
