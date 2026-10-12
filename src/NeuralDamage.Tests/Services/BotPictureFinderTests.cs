using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using NeuralDamage.Application.Queries;
using NeuralDamage.Infrastructure.Services.Attachments;
using NeuralDamage.Infrastructure.Services.BotPictures;
using NeuralDamage.Tests.Helpers;

namespace NeuralDamage.Tests.Services;

public class BotPictureFinderTests
{
    private const string PortraitThumb = "https://thumb.wikimedia.org/wikipedia/commons/thumb/e/e3/Portrait.jpg/330px-Portrait.jpg";
    private const string PortraitOriginal = "https://upload.wikimedia.org/wikipedia/commons/e/e3/Portrait.jpg";

    private static string Summary(string title, string? original = PortraitOriginal, int width = 1577, string? thumb = PortraitThumb, string type = "standard") =>
        JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["type"] = type,
            ["title"] = title,
            ["content_urls"] = new { desktop = new { page = $"https://en.wikipedia.org/wiki/{title.Replace(' ', '_')}" } },
            ["originalimage"] = original is null ? null : new { source = original, width, height = width * 4 / 3 },
            ["thumbnail"] = thumb is null ? null : new { source = thumb, width = 330, height = 440 },
        });

    private static string Commons(params (string File, string Mime, string Url)[] files) =>
        JsonSerializer.Serialize(new
        {
            query = new
            {
                pages = files.Select((f, i) => (f, i)).ToDictionary(x => (100 + x.i).ToString(), x => new
                {
                    title = "File:" + x.f.File,
                    index = x.i + 1,
                    imageinfo = new[] { new { thumburl = x.f.Url, url = x.f.Url, mime = x.f.Mime, descriptionurl = "https://commons.wikimedia.org/wiki/File:" + x.f.File } },
                }),
            },
        });

    /// <summary>Answers by URL prefix and remembers every request; anything unrouted is a 404.</summary>
    private sealed class FakeWikimedia : HttpMessageHandler
    {
        public readonly List<(string Prefix, Func<HttpResponseMessage> Respond)> Routes = [];
        public readonly List<HttpRequestMessage> Requests = [];

        public FakeWikimedia Json(string prefix, string body)
        {
            Routes.Add((prefix, () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") }));
            return this;
        }

        public FakeWikimedia Bytes(string prefix, byte[] body)
        {
            Routes.Add((prefix, () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) }));
            return this;
        }

        public FakeWikimedia Redirect(string prefix, string location)
        {
            Routes.Add((prefix, () =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.Found);
                response.Headers.Location = new Uri(location);
                return response;
            }));
            return this;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            var url = request.RequestUri!.ToString();
            var route = Routes.FirstOrDefault(r => url.StartsWith(r.Prefix, StringComparison.Ordinal));
            var response = route.Respond?.Invoke() ?? new HttpResponseMessage(HttpStatusCode.NotFound);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }

    private static BotPictureFinder Finder(FakeWikimedia fake, long maxBytes = 10 * 1024 * 1024) =>
        new(new HttpClient(fake), new MemoryCache(new MemoryCacheOptions()), new AttachmentOptions { MaxBytes = maxBytes }, NullLogger<BotPictureFinder>.Instance);

    [Test]
    public async Task FamousName_SuggestsTheLeadImage_ThenCommonsPhotos()
    {
        var fake = new FakeWikimedia()
            .Json("https://en.wikipedia.org/api/rest_v1/page/summary/Arnold_Schwarzenegger", Summary("Arnold Schwarzenegger"))
            .Json("https://commons.wikimedia.org/w/api.php", Commons(
                ("Portrait.jpg", "image/jpeg", "https://thumb.wikimedia.org/wikipedia/commons/thumb/e/e3/Portrait.jpg/960px-Portrait.jpg"),
                ("Logo.svg", "image/svg+xml", "https://upload.wikimedia.org/wikipedia/commons/1/11/Logo.svg"),
                ("Book.pdf", "application/pdf", "https://upload.wikimedia.org/wikipedia/commons/2/22/Book.pdf"),
                ("Gym.jpg", "image/jpeg", "https://thumb.wikimedia.org/wikipedia/commons/thumb/3/33/Gym.jpg/960px-Gym.jpg"),
                ("Stage.png", "image/png", "https://thumb.wikimedia.org/wikipedia/commons/thumb/4/44/Stage.png/960px-Stage.png"),
                ("Extra.jpg", "image/jpeg", "https://thumb.wikimedia.org/wikipedia/commons/thumb/5/55/Extra.jpg/960px-Extra.jpg")));

        var found = await Finder(fake).FindAsync("Arnold Schwarzenegger", CancellationToken.None);

        await Assert.That(found.Count).IsEqualTo(3);
        // A big original is asked for as a 960px thumbnail.
        await Assert.That(found[0].ImageUrl.ToString()).IsEqualTo("https://thumb.wikimedia.org/wikipedia/commons/thumb/e/e3/Portrait.jpg/960px-Portrait.jpg");
        await Assert.That(found[0].SourceUrl).IsEqualTo("https://en.wikipedia.org/wiki/Arnold_Schwarzenegger");
        // The lead image again (as a thumbnail), the SVG and the PDF are skipped.
        await Assert.That(found.Select(c => c.Title)).IsEquivalentTo(["Arnold Schwarzenegger", "Gym", "Stage"]);
        await Assert.That(found[1].SourceUrl).IsEqualTo("https://commons.wikimedia.org/wiki/File:Gym.jpg");
        // Commons is searched for the article's exact title.
        await Assert.That(fake.Requests.Last().RequestUri!.Query).Contains("gsrsearch=%22Arnold%20Schwarzenegger%22");
        await Assert.That(fake.Requests.All(r => r.Headers.UserAgent.ToString() == "NeuralDamage/1.0")).IsTrue();
    }

    [Test]
    public async Task SmallOriginal_IsUsedAsIs()
    {
        var original = "https://upload.wikimedia.org/wikipedia/en/6/6f/Megatron.jpg";
        var fake = new FakeWikimedia().Json("https://en.wikipedia.org/", Summary("Megatron", original, 223, original));

        var found = await Finder(fake).FindAsync("Megatron", CancellationToken.None);

        await Assert.That(found.Count).IsEqualTo(1);
        await Assert.That(found[0].ImageUrl.ToString()).IsEqualTo(original);
    }

    [Test]
    public async Task SvgLeadImage_IsSkipped()
    {
        var fake = new FakeWikimedia().Json("https://en.wikipedia.org/",
            Summary("Acme", "https://upload.wikimedia.org/wikipedia/commons/1/11/Acme_logo.svg", 512,
                "https://thumb.wikimedia.org/wikipedia/commons/thumb/1/11/Acme_logo.svg/330px-Acme_logo.svg.png"));

        var found = await Finder(fake).FindAsync("Acme", CancellationToken.None);

        await Assert.That(found).IsEmpty();
    }

    [Test]
    public async Task GenericPersona_GetsNoSuggestion_AndCommonsIsNotSearched()
    {
        // English has only a disambiguation page, German nothing.
        var fake = new FakeWikimedia()
            .Json("https://en.wikipedia.org/", Summary("Drunk Uncle", original: null, thumb: null, type: "disambiguation"))
            .Json("https://commons.wikimedia.org/", Commons(("Drunk Uncle Islets.jpg", "image/jpeg", "https://upload.wikimedia.org/x/Drunk_Uncle_Islets.jpg")));

        var found = await Finder(fake).FindAsync("Drunk Uncle", CancellationToken.None);

        await Assert.That(found).IsEmpty();
        await Assert.That(fake.Requests.Any(r => r.RequestUri!.Host == "commons.wikimedia.org")).IsFalse();
        await Assert.That(fake.Requests.Any(r => r.RequestUri!.Host == "de.wikipedia.org")).IsTrue();
    }

    [Test]
    public async Task GermanArticle_IsTheFallback()
    {
        var fake = new FakeWikimedia().Json("https://de.wikipedia.org/", Summary("Kommissar Rex"));

        var found = await Finder(fake).FindAsync("Kommissar Rex", CancellationToken.None);

        await Assert.That(found.Count).IsEqualTo(1);
        await Assert.That(found[0].Title).IsEqualTo("Kommissar Rex");
    }

    [Test]
    public async Task Lookups_AreCachedByName()
    {
        var fake = new FakeWikimedia().Json("https://en.wikipedia.org/", Summary("Megatron"));
        var finder = Finder(fake);

        await finder.FindAsync("Megatron", CancellationToken.None);
        var before = fake.Requests.Count;
        await finder.FindAsync("  megatron ", CancellationToken.None);

        await Assert.That(fake.Requests.Count).IsEqualTo(before);
    }

    [Test]
    [Arguments("https://upload.wikimedia.org/a.jpg", true)]
    [Arguments("https://thumb.wikimedia.org/a.jpg", true)]
    [Arguments("https://en.wikipedia.org/api/rest_v1/page/summary/X", true)]
    [Arguments("https://commons.wikimedia.org/w/api.php", true)]
    [Arguments("http://upload.wikimedia.org/a.jpg", false)]
    [Arguments("https://upload.wikimedia.org:8443/a.jpg", false)]
    [Arguments("https://user@upload.wikimedia.org/a.jpg", false)]
    [Arguments("https://upload.wikimedia.org.example.com/a.jpg", false)]
    [Arguments("https://example.com/a.jpg", false)]
    [Arguments("https://127.0.0.1/a.jpg", false)]
    [Arguments("https://fr.wikipedia.org/wiki/X", false)]
    public async Task Allowlist_OnlyLetsWikimediaThrough(string url, bool allowed)
    {
        await Assert.That(BotPictureFinder.IsAllowed(new Uri(url))).IsEqualTo(allowed);
    }

    [Test]
    public async Task LeadImage_OnAnotherHost_IsRejected()
    {
        var fake = new FakeWikimedia().Json("https://en.wikipedia.org/", Summary("Evil", "https://example.com/evil.jpg", 200, "https://example.com/evil.jpg"));

        var found = await Finder(fake).FindAsync("Evil", CancellationToken.None);

        await Assert.That(found).IsEmpty();
    }

    [Test]
    public async Task Download_RefusesRedirectsOffWikimedia()
    {
        var fake = new FakeWikimedia().Redirect("https://upload.wikimedia.org/", "https://example.com/internal");
        var candidate = new BotPictureCandidate("X", "https://en.wikipedia.org/wiki/X", new Uri("https://upload.wikimedia.org/a.jpg"));

        var image = await Finder(fake).DownloadAsync(candidate, CancellationToken.None);

        await Assert.That(image).IsNull();
        await Assert.That(fake.Requests.Any(r => r.RequestUri!.Host == "example.com")).IsFalse();
    }

    [Test]
    public async Task Download_FollowsRedirectsWithinWikimedia()
    {
        var fake = new FakeWikimedia()
            .Redirect("https://upload.wikimedia.org/", "https://thumb.wikimedia.org/a.jpg")
            .Bytes("https://thumb.wikimedia.org/", TestImages.Jpeg(960, 1280));
        var candidate = new BotPictureCandidate("X", "https://en.wikipedia.org/wiki/X", new Uri("https://upload.wikimedia.org/a.jpg"));

        var image = await Finder(fake).DownloadAsync(candidate, CancellationToken.None);

        await Assert.That(image!.ContentType).IsEqualTo("image/jpeg");
    }

    [Test]
    public async Task Download_RejectsWhatIsNotAnImage_OrTooBig()
    {
        var candidate = new BotPictureCandidate("X", "https://en.wikipedia.org/wiki/X", new Uri("https://upload.wikimedia.org/a.jpg"));

        var html = await Finder(new FakeWikimedia().Bytes("https://upload.wikimedia.org/", "<html>"u8.ToArray())).DownloadAsync(candidate, CancellationToken.None);
        var big = await Finder(new FakeWikimedia().Bytes("https://upload.wikimedia.org/", TestImages.Png(4000, 4000)), maxBytes: 16).DownloadAsync(candidate, CancellationToken.None);
        var offHost = await Finder(new FakeWikimedia()).DownloadAsync(candidate with { ImageUrl = new Uri("https://example.com/a.jpg") }, CancellationToken.None);

        await Assert.That(html).IsNull();
        await Assert.That(big).IsNull();
        await Assert.That(offHost).IsNull();
    }

    [Test]
    public async Task AcceptPath_ServesTheChosenSuggestionByIndex()
    {
        var fake = new FakeWikimedia()
            .Json("https://en.wikipedia.org/", Summary("Megatron", "https://upload.wikimedia.org/m.jpg", 300, null))
            .Bytes("https://upload.wikimedia.org/m.jpg", TestImages.Png(300, 400));
        var finder = Finder(fake);

        var suggestions = await new GetBotPictureSuggestionsHandler(finder).Handle(new GetBotPictureSuggestionsQuery("Megatron"), CancellationToken.None);
        var image = await new GetBotPictureSuggestionImageHandler(finder).Handle(new GetBotPictureSuggestionImageQuery("Megatron", 0), CancellationToken.None);
        var missing = await new GetBotPictureSuggestionImageHandler(finder).Handle(new GetBotPictureSuggestionImageQuery("Megatron", 5), CancellationToken.None);

        await Assert.That(suggestions.Value!.Single().Title).IsEqualTo("Megatron");
        await Assert.That(image.Value!.ContentType).IsEqualTo("image/png");
        await Assert.That(missing.IsFailure).IsTrue();
    }

    [Test]
    public async Task EmptyName_IsRefused()
    {
        var result = await new GetBotPictureSuggestionsHandler(Finder(new FakeWikimedia())).Handle(new GetBotPictureSuggestionsQuery("  "), CancellationToken.None);

        await Assert.That(result.IsFailure).IsTrue();
    }
}
