namespace Assistant.Api.Realtime;

internal static class UserGroups
{
    public static string For(Guid userId)
        => $"user:{userId:D}";
}
