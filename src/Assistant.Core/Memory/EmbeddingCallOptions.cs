using Assistant.Core.Models;
using Microsoft.Extensions.AI;

namespace Assistant.Core.Memory;

/// <summary>
/// Call-context properties for <see cref="EmbeddingGenerationOptions"/>.
/// The generator is a singleton; conversation/turn are per call (PT-36 D5, D9).
/// </summary>
public static class EmbeddingCallOptions
{
    public const string ConversationIdProperty = "assistant.conversationId";

    public const string TurnIdProperty = "assistant.turnId";

    public static EmbeddingGenerationOptions For(ModelCallContext context)
        => new()
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                [ConversationIdProperty] = context.ConversationId,
                [TurnIdProperty] = context.TurnId,
            },
        };

    public static ModelCallContext? TryGetCallContext(EmbeddingGenerationOptions? options)
    {
        var properties = options?.AdditionalProperties;
        if (properties is null)
        {
            return null;
        }

        if (!TryReadGuid(properties, ConversationIdProperty, out var conversationId)
            || !TryReadGuid(properties, TurnIdProperty, out var turnId))
        {
            return null;
        }

        return new ModelCallContext(conversationId, turnId);
    }

    private static bool TryReadGuid(
        AdditionalPropertiesDictionary properties, string key, out Guid value)
    {
        value = default;
        if (!properties.TryGetValue(key, out var raw) || raw is null)
        {
            return false;
        }

        if (raw is Guid guid)
        {
            value = guid;
            return guid != Guid.Empty;
        }

        if (raw is string text && Guid.TryParse(text, out guid) && guid != Guid.Empty)
        {
            value = guid;
            return true;
        }

        return false;
    }
}
