namespace Assistant.Core.Observability;

// GenAI semantic conventions (Development). Snapshot of the 2026-10-19 research.
public static class GenAiSpans
{
    public const string ExecuteTool = "execute_tool";

    public const string SearchMemory = "search_memory";

    public const string CreateMemory = "create_memory";

    public const string UpdateMemory = "update_memory";

    public const string DeleteMemory = "delete_memory";

    public const string UpsertMemory = "upsert_memory";

    public const string LoadSkill = "load_skill";

    public const string InvokeAgent = "invoke_agent";

    public const string ToolName = "gen_ai.tool.name";

    public const string ToolCallId = "gen_ai.tool.call.id";

    public const string PermissionOutcome = "vera.permission.outcome";

    public const string MemoryStoreId = "gen_ai.memory.store.id";

    public const string MemoryRecordId = "gen_ai.memory.record.id";

    public const string MemoryRecordCount = "gen_ai.memory.record.count";

    public const string SkillName = "gen_ai.skill.name";

    public const string StoreMemories = "memories";

    public const string StorePreferences = "preferences";
}
