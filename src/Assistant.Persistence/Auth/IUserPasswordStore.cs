namespace Assistant.Persistence.Auth;

public interface IUserPasswordStore
{
    public Task<string?> GetHashAsync(CancellationToken cancellationToken);

    public Task SetHashAsync(string passwordHash, CancellationToken cancellationToken);
}
