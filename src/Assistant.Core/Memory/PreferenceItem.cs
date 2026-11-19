namespace Assistant.Core.Memory;

public sealed record PreferenceItem(
    Guid Id,
    Guid UserId,
    string Key,
    string Value,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    WriteProvenance WriteProvenance);
