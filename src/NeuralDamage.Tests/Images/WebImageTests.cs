using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NeuralDamage.Application.Commands;
using NeuralDamage.Domain;
using NeuralDamage.Infrastructure.Dtos;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.Feed;
using NeuralDamage.Infrastructure.Services.Stories;
using NeuralDamage.Infrastructure.Services.WebImages;
using NeuralDamage.Tests.Helpers;
using NSubstitute;
using SkiaSharp;

namespace NeuralDamage.Tests.Images;

/// <summary>Which addresses count as the public internet.</summary>
public class PublicAddressTests
{
    [Test]
    [Arguments("127.0.0.1")]
    [Arguments("127.255.0.9")]
    [Arguments("10.1.2.3")]
    [Arguments("172.16.0.1")]
    [Arguments("172.31.255.255")]
    [Arguments("192.168.1.10")]
    [Arguments("169.254.169.254")]
    [Arguments("100.64.0.1")]
    [Arguments("0.0.0.0")]
    [Arguments("224.0.0.1")]
    [Arguments("255.255.255.255")]
    [Arguments("198.18.0.1")]
    [Arguments("192.0.2.1")]
    [Arguments("::1")]
    [Arguments("::")]
    [Arguments("fe80::1")]
    [Arguments("fc00::1")]
    [Arguments("fd12:3456::1")]
    [Arguments("ff02::1")]
    [Arguments("::ffff:127.0.0.1")]
    [Arguments("::ffff:10.0.0.1")]
    [Arguments("::ffff:169.254.169.254")]
    [Arguments("2002:7f00:1::")]
    [Arguments("64:ff9b::a00:1")]
    [Arguments("2001:db8::1")]
    public async Task PrivateAndSpecialAddresses_AreNotPublic(string address) =>
        await Assert.That(PublicAddress.IsPublic(IPAddress.Parse(address))).IsFalse();

    [Test]
    [Arguments("8.8.8.8")]
    [Arguments("1.1.1.1")]
    [Arguments("172.15.255.255")]
    [Arguments("172.32.0.1")]
    [Arguments("100.128.0.1")]
    [Arguments("2606:4700:4700::1111")]
    [Arguments("::ffff:8.8.8.8")]
    public async Task InternetAddresses_ArePublic(string address) =>
        await Assert.That(PublicAddress.IsPublic(IPAddress.Parse(address))).IsTrue();
}

/// <summary>Fetching a picture from the web without reaching anything else.</summary>
public class SafeImageFetcherTests
{
    [Test]
    public async Task APublicPicture_IsFetched_WithAGenericUserAgentAndNothingElse()
    {
        var web = new FakeWeb().Picture("https://images.example.com/cat.png", 800, 600);

        var image = await web.Fetcher.FetchAsync(new Uri("https://images.example.com/cat.png"));

        await Assert.That(image).IsNotNull();
        await Assert.That((image!.Width, image.Height, image.ContentType)).IsEqualTo((800, 600, "image/png"));
        var request = web.Requests.Single();
        await Assert.That(request.Headers.UserAgent.ToString()).IsEqualTo(SafeImageFetcher.UserAgent);
        await Assert.That(request.Headers.UserAgent.ToString()).DoesNotContain("@");
        await Assert.That(request.Headers.Contains("Cookie") || request.Headers.Contains("Authorization") || request.Headers.Contains("Referer")).IsFalse();
    }

    [Test]
    [Arguments("http://127.0.0.1/x.png")]
    [Arguments("http://10.0.0.5/x.png")]
    [Arguments("http://169.254.169.254/latest/meta-data")]
    [Arguments("http://[::1]/x.png")]
    [Arguments("http://[::ffff:192.168.0.1]/x.png")]
    [Arguments("http://localhost/x.png")]
    [Arguments("ftp://images.example.com/x.png")]
    [Arguments("file:///etc/passwd")]
    [Arguments("https://images.example.com:8443/x.png")]
    [Arguments("https://user:secret@images.example.com/x.png")]
    public async Task UrlsThatAreNotPublicHttp_AreNeverRequested(string url)
    {
        var web = new FakeWeb().Picture(url.Replace("file://", "https://x.example.com"));
        web.Dns["localhost"] = [IPAddress.Loopback];

        var image = await web.Fetcher.FetchAsync(new Uri(url));

        await Assert.That(image).IsNull();
        await Assert.That(web.Requested).IsEmpty();
    }

    [Test]
    public async Task AHostThatResolvesToAPrivateAddress_IsRefused_EvenWithAPublicOneBesideIt()
    {
        var web = new FakeWeb().Picture("https://inside.example.com/x.png").Picture("https://mixed.example.com/x.png");
        web.Dns["inside.example.com"] = [IPAddress.Parse("192.168.1.20")];
        web.Dns["mixed.example.com"] = [FakeWeb.Public, IPAddress.Parse("10.0.0.1")];

        await Assert.That(await web.Fetcher.FetchAsync(new Uri("https://inside.example.com/x.png"))).IsNull();
        await Assert.That(await web.Fetcher.FetchAsync(new Uri("https://mixed.example.com/x.png"))).IsNull();
        await Assert.That(web.Requested).IsEmpty();
    }

    [Test]
    public async Task ARedirectToAPrivateAddress_IsNotFollowed()
    {
        var web = new FakeWeb()
            .Redirect("https://images.example.com/x.png", "http://metadata.example.com/latest")
            .Picture("http://metadata.example.com/latest");
        web.Dns["metadata.example.com"] = [IPAddress.Parse("169.254.169.254")];

        var image = await web.Fetcher.FetchAsync(new Uri("https://images.example.com/x.png"));

        await Assert.That(image).IsNull();
        await Assert.That(web.Requested.Select(u => u.Host)).IsEquivalentTo(["images.example.com"]);
    }

    [Test]
    public async Task RedirectsArePublic_AndRechecked_UpToThree()
    {
        var web = new FakeWeb()
            .Redirect("https://a.example.com/1", "https://b.example.com/2")
            .Redirect("https://b.example.com/2", "https://c.example.com/3")
            .Redirect("https://c.example.com/3", "https://d.example.com/4")
            .Picture("https://d.example.com/4")
            .Redirect("https://e.example.com/1", "https://a.example.com/1");

        var three = await web.Fetcher.FetchAsync(new Uri("https://a.example.com/1"));
        var four = await web.Fetcher.FetchAsync(new Uri("https://e.example.com/1"));

        await Assert.That(three).IsNotNull();
        await Assert.That(three!.Url.Host).IsEqualTo("d.example.com");
        await Assert.That(four).IsNull();
    }

    [Test]
    public async Task TooLarge_IsRefused_WhetherItSaysSoOrNot()
    {
        var web = new FakeWeb();
        var big = new byte[SafeImageFetcher.MaxBytes + 1];
        FakeWeb.Png(10, 10, SKColors.Red).CopyTo(big, 0);
        web.Pages["https://images.example.com/declared.png"] = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(big) };
        web.Pages["https://images.example.com/streamed.png"] = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new UnknownLengthStream(big)) };

        await Assert.That(await web.Fetcher.FetchAsync(new Uri("https://images.example.com/declared.png"))).IsNull();
        await Assert.That(await web.Fetcher.FetchAsync(new Uri("https://images.example.com/streamed.png"))).IsNull();
    }

    [Test]
    public async Task OnlyRealPictures_AreTaken_NeverSvgOrWhatTheServerClaims()
    {
        var web = new FakeWeb();
        var svg = Encoding.UTF8.GetBytes("""<svg xmlns="http://www.w3.org/2000/svg" width="10" height="10"><script>alert(1)</script></svg>""");
        web.Pages["https://images.example.com/x.svg"] = () => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(svg) { Headers = { ContentType = new("image/svg+xml") } },
        };
        web.Pages["https://images.example.com/fake.png"] = () => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes("<html>not a picture</html>")) { Headers = { ContentType = new("image/png") } },
        };

        await Assert.That(await web.Fetcher.FetchAsync(new Uri("https://images.example.com/x.svg"))).IsNull();
        await Assert.That(await web.Fetcher.FetchAsync(new Uri("https://images.example.com/fake.png"))).IsNull();
    }

    [Test]
    public async Task ASlowServer_RunsOutOfTime()
    {
        var fetcher = new SafeImageFetcher(new HttpMessageInvoker(new Hanging()), NullLogger<SafeImageFetcher>.Instance,
            (_, _) => Task.FromResult(new[] { FakeWeb.Public }), TimeSpan.FromMilliseconds(200));

        var image = await fetcher.FetchAsync(new Uri("https://slow.example.com/x.png"));

        await Assert.That(image).IsNull();
    }

    [Test]
    public async Task TheRealHandler_NeverConnectsToALocalAddress()
    {
        // Something really listens here: only the connect check stands between the request and it.
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var client = new HttpClient(SafeImageFetcher.CreateHandler());

        var refused = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync($"http://127.0.0.1:{port}/"));
        var byName = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync($"http://localhost:{port}/"));

        await Assert.That(refused!.Message + refused.InnerException?.Message).Contains("public internet");
        await Assert.That(byName).IsNotNull();
        await Assert.That(listener.Pending()).IsFalse();
    }

    private sealed class Hanging : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    /// <summary>A stream that does not say how long it is, like a chunked response.</summary>
    private sealed class UnknownLengthStream(byte[] data) : MemoryStream(data)
    {
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
    }
}

/// <summary>Reading SearXNG and choosing what to use.</summary>
public class WebImageSearchTests
{
    private const string Answer = """
        {"query": "eiffel tower", "results": [
          {"title": "Eiffel Tower at night", "url": "https://pages.example.com/night", "img_src": "https://images.example.com/night.jpg", "thumbnail_src": "//thumbs.example.com/night.jpg", "resolution": "1920 x 1080", "engine": "bing images"},
          {"title": "Duplicate", "url": "https://pages.example.com/dup", "img_src": "https://images.example.com/night.jpg", "engine": "qwant images"},
          {"title": "Bad scheme", "url": "https://pages.example.com/x", "img_src": "javascript:alert(1)", "engine": "x"},
          {"title": "Tall one", "url": "https://pages.example.com/tall", "img_src": "https://images.example.com/tall.jpg", "resolution": "1080×1350", "engine": "wikicommons.images"},
          {"title": "No size", "img_src": "http://images.example.com/plain.png", "engine": "bing images"}],
         "unresponsive_engines": [["duckduckgo images", "CAPTCHA"], ["google images", "timeout"]]}
        """;

    [Test]
    public async Task TheAnswer_IsRead_WithSizes_AndFailedEnginesNamed()
    {
        using var json = JsonDocument.Parse(Answer);

        var (results, unresponsive) = SearxngImageSearch.Parse(json.RootElement);

        await Assert.That(results.Select(r => r.Title)).IsEquivalentTo(["Eiffel Tower at night", "Tall one", "No size"]);
        await Assert.That((results[0].Width, results[0].Height)).IsEqualTo((1920, 1080));
        await Assert.That(results[0].ThumbnailUrl!.AbsoluteUri).IsEqualTo("https://thumbs.example.com/night.jpg");
        await Assert.That((results[1].Width, results[1].Height)).IsEqualTo((1080, 1350));
        await Assert.That(results[2].Width).IsNull();
        await Assert.That(results[2].SourceUrl.AbsoluteUri).IsEqualTo("http://images.example.com/plain.png");
        await Assert.That(unresponsive).IsEquivalentTo(["duckduckgo images (CAPTCHA)", "google images (timeout)"]);
    }

    [Test]
    public async Task TheClient_AsksForJsonImages_AndToleratesFailure()
    {
        var calls = new List<Uri>();
        var answers = new Queue<HttpResponseMessage>([
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Answer, Encoding.UTF8, "application/json") },
            new HttpResponseMessage(HttpStatusCode.TooManyRequests),
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>captcha</html>") },
        ]);
        var http = new HttpClient(new Recording(calls, answers));
        var search = new SearxngImageSearch(http, Options.Create(new ImageSearchOptions { BaseUrl = "http://searxng.example.com:8080/" }), NullLogger<SearxngImageSearch>.Instance);

        var found = await search.SearchAsync("eiffel tower");
        var limited = await search.SearchAsync("eiffel tower");
        var broken = await search.SearchAsync("eiffel tower");

        await Assert.That(found.Count).IsEqualTo(3);
        await Assert.That(calls[0].AbsoluteUri).StartsWith("http://searxng.example.com:8080/search?q=eiffel%20tower&categories=images&format=json");
        await Assert.That(limited).IsEmpty();
        await Assert.That(broken).IsEmpty();
        var off = new SearxngImageSearch(http, Options.Create(new ImageSearchOptions()), NullLogger<SearxngImageSearch>.Instance);
        await Assert.That(off.IsEnabled).IsFalse();
        await Assert.That(await off.SearchAsync("x")).IsEmpty();
    }

    private static WebImageResult R(string name, int? width, int? height) =>
        new(name, new Uri($"https://images.example.com/{name}.png"), null, new Uri($"https://pages.example.com/{name}"), width, height, "test");

    [Test]
    public async Task Ranking_PrefersLargePicturesOfTheRightShape_AndSkipsTinyOnes()
    {
        List<WebImageResult> results = [R("tiny", 120, 90), R("wide", 1600, 900), R("tall", 1080, 1350), R("unknown", null, null), R("small", 500, 625)];

        var forPost = WebImagePicker.Rank(results, 0.8).Select(r => r.Title).ToList();
        var forBanner = WebImagePicker.Rank(results, 16 / 9.0).Select(r => r.Title).ToList();

        await Assert.That(forPost).DoesNotContain("tiny");
        await Assert.That(forPost[0]).IsEqualTo("tall");
        await Assert.That(forBanner[0]).IsEqualTo("wide");
    }

    [Test]
    public async Task TheBestThatFetches_IsUsed_BrokenAndTinyOnesAreSkipped()
    {
        var web = new FakeWeb()
            .Picture("https://images.example.com/claims-big.png", 100, 100)
            .Picture("https://images.example.com/fine.png", 900, 1100);
        web.Dns["private.example.com"] = [IPAddress.Parse("10.0.0.2")];
        List<WebImageResult> results =
        [
            R("broken", 2000, 2500),
            new("private", new Uri("https://private.example.com/x.png"), null, new Uri("https://pages.example.com/p"), 2000, 2500, "test"),
            R("claims-big", 1900, 2400),
            R("fine", 900, 1100),
        ];

        var best = await WebImagePicker.FetchBestAsync(results, 0.8, web.Fetcher);

        await Assert.That(best!.Value.Result.Title).IsEqualTo("fine");
        await Assert.That(best.Value.Image.Height).IsEqualTo(1100);
    }

    private sealed class Recording(List<Uri> calls, Queue<HttpResponseMessage> answers) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            calls.Add(request.RequestUri!);
            return Task.FromResult(answers.Dequeue());
        }
    }
}

/// <summary>Bots and people using pictures from the web.</summary>
public class WebImageUseTests
{
    private static readonly FixedTime Afternoon = new(new DateTimeOffset(2026, 7, 15, 13, 0, 0, TimeSpan.Zero));

    private static FakeWeb Web() => new FakeWeb()
        .Result("https://images.example.com/small.png", 150, 150, "Too small")
        .Result("https://images.example.com/tower.png", 1080, 1350, "Eiffel Tower", "https://thumbs.example.com/tower.png")
        .Picture("https://images.example.com/small.png", 150, 150)
        .Picture("https://images.example.com/tower.png", 1080, 1350, SKColors.Goldenrod)
        .Picture("https://thumbs.example.com/tower.png", 200, 250);

    [Test]
    public async Task StructuredOutput_WithASearch()
    {
        var (text, image) = FeedPostOutput.Parse("""{"text": "bonjour", "image": {"search": "Eiffel Tower at night"}}""");

        await Assert.That(text).IsEqualTo("bonjour");
        await Assert.That(image).IsEqualTo(new PostImageAsk(null, Search: "Eiffel Tower at night"));
        await Assert.That(FeedPostOutput.Instruction(false, false, canSearch: true)).Contains("\"search\"");
        await Assert.That(FeedPostOutput.Instruction(false, false, canSearch: false)).DoesNotContain("\"search\"");
    }

    [Test]
    public async Task ABotThatSearches_PostsTheBestPicture_WithItsSource()
    {
        var web = Web();
        using var h = await FeedHarness.CreateAsync(botCount: 1, feed: new FeedOptions { ChatInspiredShare = 0, ImageShare = 1 }, time: Afternoon, web: web);
        h.Reply("""{"text": "look where I am", "image": {"search": "Eiffel Tower"}}""");

        var post = await h.Scheduler.PostAsync(h.Bots[0], Afternoon.Now, 0.5, imageRoll: 0);

        var image = await h.Db.GeneratedImages.AsNoTracking().SingleAsync(i => i.Id == post!.ImageId);
        await Assert.That(image.Kind).IsEqualTo(GeneratedImageKind.Web);
        await Assert.That(image.SourceUrl).IsEqualTo("https://pages.example.com/1");
        await Assert.That((image.Width, image.Height)).IsEqualTo((1080, 1350));
        await h.Notifier.Received(1).PostNew(Arg.Is<PostDto>(p => p.Image != null && p.Image.SourceUrl == "https://pages.example.com/1"));
    }

    [Test]
    public async Task ABotsStory_CanUseASearchedBackground_AndCreditsItInTheSpec()
    {
        var web = Web();
        using var h = await FeedHarness.CreateAsync(botCount: 1, web: web);
        string? system = null;
        h.OpenRouter.GenerateResponseAsync(default!, default, default!, default!, default).ReturnsForAnyArgs(ci =>
        {
            system ??= ci.ArgAt<string>(2);
            return """{"background":{"color":"#000000","image":"search:Eiffel Tower"},"layers":[{"type":"text","text":"bonjour","x":50,"y":20,"size":30},{"type":"image","image":"search:nothing to find","x":50,"y":70,"size":100}]}""";
        });
        using var scope = h.Provider.CreateScope();

        var story = await scope.ServiceProvider.GetRequiredService<StoryWriter>().WriteAsync(h.Bots[0], DateTimeOffset.UtcNow);

        await Assert.That(system!).Contains("search:");
        await Assert.That(story).IsNotNull();
        var saved = StorySpecRules.Parse((await h.Db.Stories.AsNoTracking().SingleAsync()).Spec).Spec!;
        await Assert.That(saved.Background.Image!).StartsWith("generated:");
        await Assert.That(saved.Sources!).IsEquivalentTo(["https://pages.example.com/1"]);
        using var png = SKBitmap.Decode(h.Storage.StoryFiles[story!.Id]);
        await Assert.That(png.GetPixel(5, 1900)).IsEqualTo(SKColors.Goldenrod);
    }

    [Test]
    public async Task APerson_PicksAResultByItsPlace_AndGetsItWithItsSource()
    {
        var web = Web();
        using var h = await FeedHarness.CreateAsync(botCount: 1, web: web);
        using var scope = h.Provider.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<WebImageStore>();

        var found = await new SearchWebImagesHandler(store).Handle(new SearchWebImagesQuery("eiffel tower"), default);
        var thumb = await new GetWebImageThumbnailHandler(store).Handle(new GetWebImageThumbnailQuery("eiffel tower", 1), default);
        var picked = await new PickWebImageHandler(store).Handle(new PickWebImageCommand(h.User.Id, "eiffel tower", 1), default);
        var gone = await new PickWebImageHandler(store).Handle(new PickWebImageCommand(h.User.Id, "eiffel tower", 7), default);

        await Assert.That(found.Value!.Select(r => r.ThumbnailUrl)).Contains("/api/images/search/thumbnail?q=eiffel%20tower&index=1");
        using (var small = SKBitmap.Decode(thumb.Value!.Content))
            await Assert.That(small.Width).IsEqualTo(200);
        await Assert.That(picked.Value!.Kind).IsEqualTo(GeneratedImageKind.Web);
        await Assert.That(picked.Value.SourceUrl).IsEqualTo("https://pages.example.com/1");
        await Assert.That(gone.IsFailure).IsTrue();

        // It goes on the person's post like a generated picture, with its source.
        var post = await new CreatePostHandler(h.Db, h.Notifier, h.Queue).Handle(new CreatePostCommand(h.User.Id, "", ImageId: picked.Value.Id), default);
        await Assert.That(post.Value!.Image!.SourceUrl).IsEqualTo("https://pages.example.com/1");
    }

    [Test]
    public async Task AComposedPicture_WithAWebPicture_CreditsItsSource()
    {
        var web = Web();
        using var h = await FeedHarness.CreateAsync(botCount: 1, web: web);
        using var scope = h.Provider.CreateScope();
        var picked = await scope.ServiceProvider.GetRequiredService<WebImageStore>().PickAsync(h.User.Id, "eiffel tower", 1);
        var spec = new StorySpec(new StoryBackground("#000000", $"generated:{picked.Value!.Id}", "cover"), [], "post-4x5", Sources: ["https://evil.example.com/made-up"]);

        var composed = await scope.ServiceProvider.GetRequiredService<ImageComposer>().ComposeAsync(spec, h.User.Id, null);

        await Assert.That(composed.Value!.SourceUrl).IsEqualTo("https://pages.example.com/1");
        await Assert.That(composed.Value.Spec!).DoesNotContain("evil");
    }
}
