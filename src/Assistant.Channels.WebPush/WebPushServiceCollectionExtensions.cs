using System.Net;
using System.Net.Sockets;
using Assistant.Abstractions.Channels;
using Assistant.Abstractions.Notifications;
using Assistant.Abstractions.Push;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Assistant.Channels.WebPush;

public static class WebPushServiceCollectionExtensions
{
    /// <summary>
    /// Binds and validates <see cref="WebPushOptions"/>. Registers the outbound channel only when
    /// <c>WebPush:Enabled</c> is true.
    /// </summary>
    public static IServiceCollection AddWebPushChannel(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSingleton<IValidateOptions<WebPushOptions>>(new WebPushOptionsValidator());
        services.AddOptions<WebPushOptions>()
            .Bind(configuration.GetSection(WebPushOptions.SectionName))
            .ValidateOnStart();

        if (!IsEnabled(configuration))
        {
            return services;
        }

        var http = services.AddHttpClient(WebPushOutboundChannel.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        http.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            ConnectCallback = ConnectPushServiceAsync,
        });
        http.AddHttpMessageHandler<WebPushOutboundHandler>();
        http.RemoveAllLoggers();
        services.AddTransient<WebPushOutboundHandler>();

        services.AddSingleton(new WebPushPublicOrigin(configuration["Channels:PublicBaseUrl"]));
        services.AddScoped<WebPushOutboundChannel>(sp => new WebPushOutboundChannel(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(WebPushOutboundChannel.HttpClientName),
            sp.GetRequiredService<IPushSubscriptionReader>(),
            sp.GetRequiredService<INotificationStore>(),
            sp.GetRequiredService<IOptions<WebPushOptions>>(),
            sp.GetRequiredService<WebPushPublicOrigin>().BaseUrl,
            sp.GetRequiredService<ILogger<WebPushOutboundChannel>>()));
        services.AddScoped<IOutboundChannel>(sp => sp.GetRequiredService<WebPushOutboundChannel>());
        return services;
    }

    private static bool IsEnabled(IConfiguration configuration)
    {
        var raw = configuration[$"{WebPushOptions.SectionName}:Enabled"];
        return bool.TryParse(raw, out var enabled) && enabled;
    }

    /// <summary>
    /// Dials only addresses that are not loopback, private, or link-local.
    /// The message names neither the host nor the address.
    /// </summary>
    private static async ValueTask<Stream> ConnectPushServiceAsync(
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        var dns = context.DnsEndPoint;
        IPAddress[] addresses;
        try
        {
            if (PushEndpointGuard.TryHostLiteral(dns.Host, out var literal) && literal is not null)
            {
                addresses = [literal];
            }
            else
            {
                addresses = await Dns.GetHostAddressesAsync(dns.Host, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is SocketException or ArgumentException)
        {
            throw new HttpRequestException("The push service address could not be resolved.", exception);
        }

        foreach (var address in addresses)
        {
            if (PushEndpointGuard.IsBlockedAddress(address))
            {
                continue;
            }

            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(address, dns.Port, cancellationToken).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (OperationCanceledException)
            {
                socket.Dispose();
                throw;
            }
            catch (Exception exception)
            {
                socket.Dispose();
                throw new HttpRequestException("The push service connection failed.", exception);
            }
        }

        throw new HttpRequestException("The push service resolved to a blocked address.");
    }
}

/// <summary>Public origin used to build the notification <c>navigate</c> URL.</summary>
internal sealed class WebPushPublicOrigin(string? baseUrl)
{
    public string? BaseUrl { get; } = string.IsNullOrWhiteSpace(baseUrl) ? null : baseUrl.Trim().TrimEnd('/');
}
