namespace Assistant.Persistence.Auth;

public interface IDeviceStore
{
    public Task<DeviceRow> AddAsync(string name, byte[] refreshTokenHash, CancellationToken cancellationToken);

    public Task<DeviceRow?> RotateAsync(
        byte[] currentHash,
        byte[] newHash,
        DateTimeOffset now,
        TimeSpan maxAge,
        CancellationToken cancellationToken);

    public Task<bool> RevokeAsync(Guid id, DateTimeOffset now, CancellationToken cancellationToken);
}

public sealed record DeviceRow(
    Guid Id,
    string Name,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastSeenAt,
    DateTimeOffset? RevokedAt);
