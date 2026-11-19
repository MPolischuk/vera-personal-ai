namespace Assistant.Api.Audit;

internal static class InProcessToolNames
{
    internal static readonly string[] All =
    [
        "get_current_datetime",
        "get_assistant_info",
        "echo_with_confirmation",
        "remember",
        "recall",
        "search_memory",
        "forget",
        "get_preferences",
        "update_preference",
        "load_skill",
        "propose_skill",
        "create_reminder",
        "list_reminders",
        "cancel_reminder",
        "list_tasks",
        "cancel_task",
        "delegate_work",
        "set_goal",
        "complete_goal",
        "list_goals",
        "set_phrase_shortcut",
        "list_automations",
        "create_automation",
        "pause_automation",
        "delete_automation",
        "set_heartbeat_checklist",
        "list_heartbeat_checklist",
        "set_standing_order",
        "pause_standing_order",
        "list_standing_orders",
        "set_conditional_intent",
        "pause_conditional_intent",
        "list_conditional_intents",
    ];
}
