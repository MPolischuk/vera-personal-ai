using System.Text.Json;
using System.Text.Json.Serialization;
using Assistant.Abstractions;
using Assistant.Core.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Assistant.Core.Personality;

internal sealed class PersonalityService(
    IPersonalityProfileProvider provider,
    IServiceScopeFactory scopes,
    ITokenEstimator estimator,
    IOptions<ContextOptions> context,
    ILogger<PersonalityService> log) : IPersonalityService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly Lock _lock = new();
    private Task<ActivePersonality>? _load;

    public ValueTask<ActivePersonality> GetActiveAsync(CancellationToken ct)
    {
        // One load per process. The exception is cached too, and that is fine:
        // a broken profile is fixed by editing the file and restarting, not by retrying (D12).
        // Do not link ct to the cached Task: canceling a turn must not leave the load half-done.
        lock (_lock)
        {
            _load ??= LoadAsync();
        }

        return new ValueTask<ActivePersonality>(_load);
    }

    private async Task<ActivePersonality> LoadAsync()
    {
        var profile = await provider.LoadAsync(CancellationToken.None);
        var prompt = PersonalityPromptComposer.Compose(profile);
        var tokens = estimator.Estimate(prompt);

        PersonalityProfileValidator.ThrowIfInvalid(profile, tokens, context.Value.TokenBudget);

        // Own scope: the store is scoped and this service is singleton (PT-03 D15).
        using (var scope = scopes.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IPersonalityStore>();
            await store.SaveActiveAsync(
                WellKnownIds.DefaultUser, profile.Name, Serialize(profile), CancellationToken.None);
        }

        PersonalityLog.ActiveProfile(log, profile.Name, tokens);

        return new ActivePersonality(profile, prompt, tokens);
    }

    internal static string Serialize(PersonalityProfile profile)
        => JsonSerializer.Serialize(profile, JsonOptions);
}
