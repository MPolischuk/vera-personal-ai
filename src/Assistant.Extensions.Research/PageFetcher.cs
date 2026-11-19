using System.Net;
using System.Net.Sockets;
using Assistant.Abstractions.Net;
using Microsoft.Extensions.Options;

namespace Assistant.Extensions.Research;

/// <summary>
/// Downloads one page in-process. Redirects are followed by hand, and every hop is checked
/// against <see cref="OutboundAddressPolicy"/> before the next request.
/// </summary>
public sealed class PageFetcher
{
    public const string HttpClientName = "research.page";

    public const string BlockedMessage = "URL bloqueada por política";

    public const string OverCapMessage = "la lectura superó el tope";

    private readonly HttpClient _http;
    private readonly ResearchOptions _options;

    public PageFetcher(HttpClient httpClient, IOptions<ResearchOptions> options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        _http = httpClient;
        _options = options.Value;
    }

    public async Task<PageFetchResult> FetchAsync(Uri url, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(url);
        var current = url;
        var redirects = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (OutboundAddressPolicy.IsBlockedTarget(current))
            {
                return PageFetchResult.Blocked();
            }

            HttpResponseMessage response;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, current);
                response = await _http
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return PageFetchResult.OverCap();
            }
            catch (HttpRequestException)
            {
                return PageFetchResult.Blocked();
            }
            catch (IOException)
            {
                return PageFetchResult.Blocked();
            }
            catch (SocketException)
            {
                return PageFetchResult.Blocked();
            }

            using (response)
            {
                if (IsRedirect(response.StatusCode))
                {
                    if (redirects >= _options.MaxRedirects || response.Headers.Location is null)
                    {
                        return PageFetchResult.OverCap();
                    }

                    redirects++;
                    current = NextHop(current, response.Headers.Location);
                    continue;
                }

                return await ReadBodyAsync(response, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Dials only when every resolved address is allowed. One blocked address cancels the connection.
    /// The message names neither the host nor the address.
    /// </summary>
    internal static async ValueTask<Stream> ConnectAsync(
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        var host = NormalizeHost(context.DnsEndPoint.Host);
        if (OutboundAddressPolicy.IsBlockedHost(host))
        {
            throw new HttpRequestException(BlockedMessage);
        }

        IPAddress[] addresses;
        try
        {
            if (IPAddress.TryParse(host, out var literal))
            {
                addresses = [literal];
            }
            else
            {
                addresses = await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is SocketException or ArgumentException)
        {
            throw new HttpRequestException(BlockedMessage, exception);
        }

        if (AnyAddressBlocked(addresses))
        {
            throw new HttpRequestException(BlockedMessage);
        }

        Exception? last = null;
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(address, context.DnsEndPoint.Port, cancellationToken).ConfigureAwait(false);
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
                last = exception;
            }
        }

        throw new HttpRequestException(BlockedMessage, last);
    }

    internal static bool AnyAddressBlocked(IReadOnlyList<IPAddress> addresses)
    {
        ArgumentNullException.ThrowIfNull(addresses);
        if (addresses.Count == 0)
        {
            return true;
        }

        foreach (var address in addresses)
        {
            if (OutboundAddressPolicy.IsBlocked(address))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<PageFetchResult> ReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var max = _options.MaxPageBytes;
        if (max <= 0)
        {
            return PageFetchResult.OverCap();
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var body = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if ((long)body.Length + read > max)
            {
                return PageFetchResult.OverCap();
            }

            body.Write(buffer.AsSpan(0, read));
        }

        return PageFetchResult.Ok(body.ToArray(), response.Content.Headers.ContentType?.MediaType);
    }

    private static bool IsRedirect(HttpStatusCode status)
        => status is HttpStatusCode.MovedPermanently
            or HttpStatusCode.Redirect
            or HttpStatusCode.RedirectMethod
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect;

    private static Uri NextHop(Uri current, Uri location)
        => location.IsAbsoluteUri ? location : new Uri(current, location);

    private static string NormalizeHost(string host)
    {
        var text = host.Trim().TrimEnd('.');
        if (text.Length >= 2 && text[0] == '[' && text[^1] == ']')
        {
            text = text[1..^1];
        }

        return text;
    }
}

/// <summary>Body of a page, or one of the two fixed failure texts.</summary>
public sealed record PageFetchResult(bool Succeeded, byte[]? Body, string? MediaType, string? Error)
{
    public static PageFetchResult Ok(byte[] body, string? mediaType)
        => new(true, body, mediaType, null);

    public static PageFetchResult Blocked()
        => new(false, null, null, PageFetcher.BlockedMessage);

    public static PageFetchResult OverCap()
        => new(false, null, null, PageFetcher.OverCapMessage);
}
