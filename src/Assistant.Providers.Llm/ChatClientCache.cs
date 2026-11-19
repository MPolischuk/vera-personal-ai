using System.Collections.Concurrent;
using Assistant.Providers.Llm.Configuration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Assistant.Providers.Llm;

/// <summary>
/// One client per connection (provider + endpoint + key + model). Owns the clients
/// and disposes them when discarded (D5).
/// </summary>
internal sealed class ChatClientCache(ILoggerFactory loggers) : IDisposable
{
    private readonly ConcurrentDictionary<ConnectionKey, Lazy<IChatClient>> _clients = new();
    private bool _disposed;

    public IChatClient Get(ModelProfileOptions profile)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var key = ConnectionKey.From(profile);
        var lazy = _clients.GetOrAdd(
            key,
            static (_, state) => new Lazy<IChatClient>(
                () => ChatClientFactory.Create(state.Profile, state.Loggers)),
            (Profile: profile, Loggers: loggers));

        return lazy.Value;
    }

    /// <summary>How many distinct connections are cached. Tests only.</summary>
    internal int Count => _clients.Count;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var entry in _clients.Values)
        {
            if (entry.IsValueCreated)
            {
                entry.Value.Dispose();
            }
        }

        _clients.Clear();
    }

    private readonly record struct ConnectionKey(
        string Provider, string Endpoint, string ApiKey, string Model)
    {
        public static ConnectionKey From(ModelProfileOptions profile)
            => new(profile.Provider, profile.Endpoint, profile.ApiKey, profile.Model);
    }
}
