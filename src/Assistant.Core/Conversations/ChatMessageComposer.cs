using Assistant.Abstractions.Context;
using Assistant.Core.Context;
using Microsoft.Extensions.AI;

namespace Assistant.Core.Conversations;

internal static class ChatMessageComposer
{
    public static List<ChatMessage> Compose(
        AssembledContext context,
        IReadOnlyList<ConversationMessage> history,
        string userMessage)
        => ComposeCore(context, history, userMessage);

    /// <summary>
    /// Resume path: history already includes the original user turn; do not append another.
    /// </summary>
    public static List<ChatMessage> ComposeResume(
        AssembledContext context,
        IReadOnlyList<ConversationMessage> history)
        => ComposeCore(context, history, userMessage: null);

    private static List<ChatMessage> ComposeCore(
        AssembledContext context,
        IReadOnlyList<ConversationMessage> history,
        string? userMessage)
    {
        var messages = new List<ChatMessage>();

        foreach (var group in GroupByPlacement(context))
        {
            switch (group.Key)
            {
                case ContextPlacement.SystemPrompt:
                case ContextPlacement.PrependedMessage:
                    Add(messages, ChatRole.System, Join(group));
                    break;

                case ContextPlacement.AppendedMessage:
                    // One system message per fragment so character.cue sits alone
                    // immediately before the user turn.
                    foreach (var fragment in group)
                    {
                        Add(messages, ChatRole.System, fragment.Content);
                    }

                    break;

                case ContextPlacement.ConversationHistory:
                    foreach (var fragment in group)
                    {
                        // D5 positional contract: the last Parts.Count entries survived.
                        foreach (var message in history.TakeLast(fragment.Parts.Count))
                        {
                            messages.Add(new ChatMessage(RoleMapper.ToChatRole(message.Role), message.Content));
                        }
                    }

                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(context), group.Key, "Unknown context placement.");
            }
        }

        if (userMessage is not null)
        {
            messages.Add(new ChatMessage(ChatRole.User, userMessage));
        }

        return messages;
    }

    // AssembledContext appearance order is already by priority (PT-03 T-03.5).
    // Group without reordering: each placement keeps its fragments' relative order.
    private static IEnumerable<IGrouping<ContextPlacement, ContextFragment>> GroupByPlacement(
        AssembledContext context)
    {
        // Fixed emission order: SystemPrompt → Prepended → History → Appended → User.
        ContextPlacement[] order =
        [
            ContextPlacement.SystemPrompt,
            ContextPlacement.PrependedMessage,
            ContextPlacement.ConversationHistory,
            ContextPlacement.AppendedMessage,
        ];

        foreach (var placement in order)
        {
            var fragments = context.Fragments.Where(f => f.Placement == placement).ToList();
            if (fragments.Count > 0)
            {
                yield return new Group(placement, fragments);
            }
        }
    }

    private static string Join(IEnumerable<ContextFragment> fragments)
        => string.Join("\n\n", fragments.Select(f => f.Content));

    private static void Add(List<ChatMessage> messages, ChatRole role, string text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            messages.Add(new ChatMessage(role, text));
        }
    }

    private sealed class Group(ContextPlacement key, IReadOnlyList<ContextFragment> items)
        : IGrouping<ContextPlacement, ContextFragment>
    {
        public ContextPlacement Key => key;

        public IEnumerator<ContextFragment> GetEnumerator() => items.GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
