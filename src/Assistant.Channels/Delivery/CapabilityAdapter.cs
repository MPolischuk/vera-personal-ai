using Assistant.Abstractions.Channels;

namespace Assistant.Channels.Delivery;

/// <summary>Deterministic adaptation of a notification to a channel. No model calls.</summary>
public static class CapabilityAdapter
{
    public const string ReminderHint = "Respondé \"listo\" o abrí la app";

    public static OutboundAdaptation Adapt(
        OutboundMessage message,
        ChannelCapabilities capabilities,
        ChannelReachability reachability)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentNullException.ThrowIfNull(reachability);

        if (capabilities.RequiresTemplatesOutsideWindow && !reachability.InsideServiceWindow)
        {
            return new OutboundAdaptation.NotApplicable("template_missing");
        }

        var text = message.Text;
        var actions = message.Actions;
        if (actions is { Count: > 0 } && !capabilities.SupportsNotificationActions)
        {
            if (capabilities.SupportsInbound && IsReminderActions(actions))
            {
                text = string.IsNullOrEmpty(text) ? ReminderHint : text + "\n\n" + ReminderHint;
            }

            actions = null;
        }

        var silent = message.Silent && capabilities.SupportsSilentDelivery;
        return new OutboundAdaptation.Ready(message with
        {
            Text = text,
            Actions = actions,
            Silent = silent,
        });
    }

    private static bool IsReminderActions(IReadOnlyList<OutboundAction> actions)
        => actions.All(action =>
            action.ActionId is "done"
            || action.ActionId.StartsWith("snooze:", StringComparison.Ordinal));
}

/// <summary>Result of adapting one message to one channel.</summary>
public abstract record OutboundAdaptation
{
    public sealed record Ready(OutboundMessage Message) : OutboundAdaptation;

    public sealed record NotApplicable(string Reason) : OutboundAdaptation;
}
