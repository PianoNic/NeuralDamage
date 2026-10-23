using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using NeuralDamage.Infrastructure.Services.WebImages;
using SkiaSharp;

namespace NeuralDamage.Tests.Helpers;

/// <summary>
/// The web for tests: a search that returns what the test says, hosts that
/// resolve to what the test says (public by default), and pages that answer
/// what the test says. Nothing leaves the machine.
/// </summary>
public sealed class FakeWeb
{
    /// <summary>A public address that is never actually connected to.</summary>
    public static readonly IPAddress Public = IPAddress.Parse("8.8.4.4");

    public List<WebImageResult> Results { get; } = [];
    public Dictionary<string, IPAddress[]> Dns { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, Func<HttpResponseMessage>> Pages { get; } = new(StringComparer.Ordinal);
    public List<Uri> Requested { get; } = [];
    public List<HttpRequestMessage> Requests { get; } = [];

    public IWebImageSearch Search { get; }
    public SafeImageFetcher Fetcher { get; }

    public FakeWeb()
    {
        Search = new FakeSearch(this);
        Fetcher = new SafeImageFetcher(new HttpMessageInvoker(new Handler(this)), NullLogger<SafeImageFetcher>.Instance, ResolveAsync);
    }

    private Task<IPAddress[]> ResolveAsync(string host, CancellationToken ct) =>
        Task.FromResult(IPAddress.TryParse(host.Trim('[', ']'), out var literal) ? [literal] : Dns.GetValueOrDefault(host) ?? [Public]);

    /// <summary>Serves a picture at <paramref name="url"/>.</summary>
    public FakeWeb Picture(string url, int width = 1200, int height = 900, SKColor? color = null)
    {
        var png = Png(width, height, color ?? SKColors.SteelBlue);
        Pages[url] = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(png) };
        return this;
    }

    public FakeWeb Redirect(string from, string to)
    {
        Pages[from] = () => new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri(to) } };
        return this;
    }

    public FakeWeb Result(string imageUrl, int? width = 1200, int? height = 900, string title = "A picture", string? thumbnail = null)
    {
        Results.Add(new WebImageResult(title, new Uri(imageUrl), thumbnail is null ? null : new Uri(thumbnail), new Uri("https://pages.example.com/" + Results.Count), width, height, "test"));
        return this;
    }

    public static byte[] Png(int width, int height, SKColor color)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
            canvas.Clear(color);
        using var data = SKImage.FromBitmap(bitmap).Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private sealed class FakeSearch(FakeWeb web) : IWebImageSearch
    {
        public bool IsEnabled => true;

        public Task<IReadOnlyList<WebImageResult>> SearchAsync(string query, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<WebImageResult>>(web.Results.ToList());
    }

    private sealed class Handler(FakeWeb web) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (web.Requested)
            {
                web.Requested.Add(request.RequestUri!);
                web.Requests.Add(request);
            }
            return Task.FromResult(web.Pages.TryGetValue(request.RequestUri!.AbsoluteUri, out var page)
                ? page()
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
