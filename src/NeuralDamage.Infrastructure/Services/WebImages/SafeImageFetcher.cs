using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using NeuralDamage.Infrastructure.Services.Attachments;

namespace NeuralDamage.Infrastructure.Services.WebImages;

/// <summary>A picture fetched from the web, checked by its bytes.</summary>
/// <param name="Url">Where it finally came from, after redirects.</param>
public record FetchedImage(byte[] Content, string ContentType, int Width, int Height, Uri Url);

/// <summary>
/// Fetches a picture from somewhere on the web without letting the address
/// reach anything the server should not: http(s) on the default port only,
/// every host resolved and every address it has checked to be public before
/// each request and again when the socket connects (so a host cannot change
/// its answer in between), at most <see cref="MaxRedirects"/> redirects, each
/// checked the same way, at most <see cref="MaxBytes"/>, a deadline, and a
/// PNG, JPEG, WebP or GIF by its bytes (never SVG, never whatever the server
/// claims). The request carries a generic User-Agent and nothing else about
/// the app, its users or the machine.
/// </summary>
public class SafeImageFetcher(
    HttpMessageInvoker http,
    ILogger<SafeImageFetcher> logger,
    Func<string, CancellationToken, Task<IPAddress[]>>? resolve = null,
    TimeSpan? timeout = null)
{
    public const int MaxRedirects = 3;
    public const long MaxBytes = 8L * 1024 * 1024;
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

    private readonly TimeSpan _timeout = timeout ?? DefaultTimeout;

    /// <summary>Generic, with nothing about the user or the server it runs on.</summary>
    public const string UserAgent = "Mozilla/5.0 (compatible; NeuralDamage/1.0)";

    private readonly Func<string, CancellationToken, Task<IPAddress[]>> _resolve = resolve ?? Resolve;

    /// <summary>
    /// The handler the fetcher's client must use in production: no automatic
    /// redirects, no proxy, no cookies, and a connect step that resolves the
    /// host itself and only ever opens a socket to a public address.
    /// </summary>
    public static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false,
        UseProxy = false,
        UseCookies = false,
        AutomaticDecompression = DecompressionMethods.None,
        ConnectTimeout = TimeSpan.FromSeconds(5),
        PooledConnectionLifetime = TimeSpan.FromMinutes(1),
        ConnectCallback = ConnectAsync,
    };

    /// <summary>Opens a socket only to a public address of the host; refuses when it has any other.</summary>
    public static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken ct)
    {
        var addresses = await Resolve(context.DnsEndPoint.Host, ct);
        if (addresses.Length == 0 || !addresses.All(PublicAddress.IsPublic))
            throw new HttpRequestException($"{context.DnsEndPoint.Host} is not on the public internet.");

        Exception? last = null;
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), ct);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception ex) when (ex is SocketException)
            {
                socket.Dispose();
                last = ex;
            }
        }
        throw new HttpRequestException($"Could not connect to {context.DnsEndPoint.Host}.", last);
    }

    private static async Task<IPAddress[]> Resolve(string host, CancellationToken ct) =>
        IPAddress.TryParse(host.Trim('[', ']'), out var literal) ? [literal] : await Dns.GetHostAddressesAsync(host, ct);

    /// <summary>Whether <paramref name="uri"/> may be asked for at all, before its host is resolved.</summary>
    public static bool IsAllowedUrl(Uri uri) =>
        uri.IsAbsoluteUri
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
        && uri.IsDefaultPort
        && string.IsNullOrEmpty(uri.UserInfo)
        && uri.HostNameType is UriHostNameType.Dns or UriHostNameType.IPv4 or UriHostNameType.IPv6
        && !uri.IsLoopback;

    /// <summary>The picture at <paramref name="url"/>, or null with a reason in the log when it is not one the server may fetch.</summary>
    public async Task<FetchedImage?> FetchAsync(Uri url, CancellationToken ct = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(_timeout);
        try
        {
            return await FetchCheckedAsync(url, deadline.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogInformation("Web picture {Host} took too long", url.Host);
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or SocketException or IOException)
        {
            logger.LogInformation("Web picture {Host} could not be fetched: {Reason}", url.Host, ex.Message);
            return null;
        }
    }

    private async Task<FetchedImage?> FetchCheckedAsync(Uri url, CancellationToken ct)
    {
        for (var hop = 0; hop <= MaxRedirects; hop++)
        {
            if (!await IsAllowedAsync(url, ct))
            {
                logger.LogInformation("Web picture refused: {Host} is not a public http(s) address", url.Host);
                return null;
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd(UserAgent);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("image/*"));
            using var response = await http.SendAsync(request, ct);

            if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther
                or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
            {
                if (response.Headers.Location is not { } location)
                    return null;
                url = location.IsAbsoluteUri ? location : new Uri(url, location);
                continue;
            }
            if (!response.IsSuccessStatusCode)
            {
                logger.LogInformation("Web picture {Host} answered {Status}", url.Host, (int)response.StatusCode);
                return null;
            }

            var data = await ReadCappedAsync(response, ct);
            if (data is null)
            {
                logger.LogInformation("Web picture {Host} is larger than {Max} MB", url.Host, MaxBytes / (1024 * 1024));
                return null;
            }
            if (ImageInspector.Inspect(data) is not { Width: { } width, Height: { } height } image)
            {
                logger.LogInformation("Web picture {Host} is not a PNG, JPEG, WebP or GIF", url.Host);
                return null;
            }
            return new FetchedImage(data, image.ContentType, width, height, url);
        }
        logger.LogInformation("Web picture refused: more than {Max} redirects", MaxRedirects);
        return null;
    }

    private async Task<bool> IsAllowedAsync(Uri url, CancellationToken ct)
    {
        if (!IsAllowedUrl(url))
            return false;
        try
        {
            var addresses = await _resolve(url.IdnHost, ct);
            return addresses.Length > 0 && addresses.All(PublicAddress.IsPublic);
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private static async Task<byte[]?> ReadCappedAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.Content.Headers.ContentLength > MaxBytes)
            return null;
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > MaxBytes)
                return null;
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }
}
