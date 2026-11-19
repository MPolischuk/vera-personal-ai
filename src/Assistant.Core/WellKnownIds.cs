namespace Assistant.Core;

public static class WellKnownIds
{
    /// <summary>
    /// Sole V1 user, seeded by the initial migration (PT-02, D11).
    /// Stops being a special value once multi-user management exists.
    /// </summary>
    public static readonly Guid DefaultUser = new("00000000-0000-0000-0000-000000000001");
}
