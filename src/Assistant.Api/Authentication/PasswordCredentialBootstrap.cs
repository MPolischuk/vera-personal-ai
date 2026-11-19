using Assistant.Api.Configuration;
using Microsoft.Extensions.Options;

namespace Assistant.Api.Authentication;

internal sealed class PasswordCredentialBootstrap(
    IOptions<AuthOptions> options,
    IServiceScopeFactory scopes) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (options.Value.Mode != AuthMode.Password)
        {
            return;
        }

        await using var scope = scopes.CreateAsyncScope();
        var credentials = scope.ServiceProvider.GetRequiredService<PasswordCredentialService>();
        await credentials.EnsureInitialAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
