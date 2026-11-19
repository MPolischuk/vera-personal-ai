using System.Text.Json;
using Assistant.Channels.WebPush;

namespace Assistant.Api.Cli;

internal static class VapidKeysCommand
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static bool TryDispatch(
        string[] args,
        out int exitCode,
        TextWriter? output = null,
        TextWriter? error = null)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length == 0 || args[0] != "vapid-keys")
        {
            exitCode = 0;
            return false;
        }

        if (args is not ["vapid-keys"])
        {
            (error ?? Console.Error).WriteLine("usage: dotnet Assistant.Api.dll vapid-keys");
            exitCode = 2;
            return true;
        }

        var (publicKey, privateKey) = VapidKeys.Generate();
        var json = JsonSerializer.Serialize(new VapidKeyPair(publicKey, privateKey), Json);
        (output ?? Console.Out).WriteLine(json);
        exitCode = 0;
        return true;
    }

    private sealed record VapidKeyPair(string PublicKey, string PrivateKey);
}
