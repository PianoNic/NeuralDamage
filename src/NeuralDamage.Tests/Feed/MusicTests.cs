using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NeuralDamage.Application.Commands;
using NeuralDamage.Application.Queries;
using NeuralDamage.Domain;
using NeuralDamage.Infrastructure.Dtos;
using NeuralDamage.Infrastructure.Mappers;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.Feed;
using NeuralDamage.Infrastructure.Services.Music;
using NeuralDamage.Infrastructure.Services.Stories;
using NeuralDamage.Tests.Helpers;
using NSubstitute;

namespace NeuralDamage.Tests.Feed;

public class MusicTests
{
    [Test]
    public async Task ADeezerTrack_IsReadWithItsShortTitleCoverAndPreview()
    {
        using var doc = JsonDocument.Parse("""
            {"id":107465566,"readable":true,"title":"Space Oddity (2015 Remaster)","title_short":"Space Oddity","duration":318,
             "preview":"https://cdnt-preview.dzcdn.net/a.mp3","artist":{"name":"David Bowie"},
             "album":{"title":"David Bowie","cover_big":"https://cdn-images.dzcdn.net/b.jpg"}}
            """);

        var track = DeezerClient.Track(doc.RootElement);

        await Assert.That(track).IsEqualTo(new DeezerTrack(107465566, "Space Oddity", "David Bowie", "David Bowie", "https://cdn-images.dzcdn.net/b.jpg", 318, "https://cdnt-preview.dzcdn.net/a.mp3"));
        using var locked = JsonDocument.Parse("""{"id":1,"readable":false,"title":"x","artist":{"name":"y"}}""");
        await Assert.That(DeezerClient.Track(locked.RootElement)).IsNull();
    }

    [Test]
    public async Task YtDlp_SearchesForASongOfAboutTheRightLength_AndOnlyKeepsWhatItSaved()
    {
        var folder = Path.Combine(Path.GetTempPath(), "nd-ytdlp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, "song.m4a");
        await File.WriteAllBytesAsync(file, [1]);

        var arguments = YtDlpSongFetcher.Arguments("David Bowie", "Space Oddity", 318, folder, "cache", 25);
        var found = YtDlpSongFetcher.Parse($"oops\n305.4|m4a|{file}\n", folder);
        var outside = YtDlpSongFetcher.Parse($"300|m4a|{Path.GetTempFileName()}", folder);

        await Assert.That(arguments[^1]).IsEqualTo("ytsearch5:David Bowie - Space Oddity audio");
        await Assert.That(arguments).Contains("duration>=292 & duration<=344");
        await Assert.That(found).IsEqualTo(new FullSong(Path.GetFullPath(file), "audio/mp4", 305));
        await Assert.That(outside).IsNull();
        Directory.Delete(folder, true);
    }

    [Test]
    public async Task APickedSong_IsStoredOnceWithItsPreview_AndItsWholeSongFollows()
    {
        using var db = TestDbContext.Create();
        var store = TestMusic.Store();
        var full = new FakeFullSongs(store);
        var queue = new SongFetchQueue(null!, Microsoft.Extensions.Logging.Abstractions.NullLogger<SongFetchQueue>.Instance);
        var library = TestMusic.Library(db, store: store, queue: queue, fetcher: full);

        var song = await library.AddAsync(1, wantFull: true);
        var again = await library.AddAsync(1, wantFull: true);

        await Assert.That(song!.Id).IsEqualTo(again!.Id);
        await Assert.That(await db.Songs.CountAsync()).IsEqualTo(1);
        await Assert.That(song.HasPreview).IsTrue();
        await Assert.That(File.Exists(store.PathOf(song.Id, full: false))).IsTrue();
        await Assert.That(library.ToDto(song).FullPending).IsTrue();

        await Assert.That(await library.FetchFullAsync(song.Id)).IsTrue();
        var dto = library.ToDto(song);
        await Assert.That(dto.FullUrl).IsEqualTo($"/api/music/songs/{song.Id}/full");
        await Assert.That(dto.FullSeconds).IsEqualTo(319);
        await Assert.That(File.Exists(store.PathOf(song.Id, full: true))).IsTrue();
    }

    [Test]
    public async Task AWholeSongThatIsntFound_IsTriedAgainOnlyADayLater()
    {
        using var db = TestDbContext.Create();
        var store = TestMusic.Store();
        var full = new FakeFullSongs(store) { Finds = false };
        var library = TestMusic.Library(db, store: store, fetcher: full);
        var song = (await library.AddAsync(2, wantFull: false))!;

        await Assert.That(await library.FetchFullAsync(song.Id)).IsFalse();

        await Assert.That(song.FullFailedAt).IsNotNull();
        await Assert.That(library.ShouldFetchFull(song, DateTime.UtcNow.AddHours(1))).IsFalse();
        await Assert.That(library.ShouldFetchFull(song, DateTime.UtcNow.AddDays(1.1))).IsTrue();
        full.IsAvailable = false;
        await Assert.That(library.ShouldFetchFull(song, DateTime.UtcNow.AddDays(2))).IsFalse();
    }

    [Test]
    public async Task ASongWithNoAudio_CantBeAdded()
    {
        using var db = TestDbContext.Create();
        var library = TestMusic.Library(db, new FakeDeezer { PreviewsFail = true });

        await Assert.That(await library.AddAsync(1, wantFull: false)).IsNull();
        await Assert.That(await library.AddAsync(999, wantFull: false)).IsNull();
    }

    [Test]
    public async Task AClip_StartsInsideTheWholeSong_AndWithoutItThePreviewPlays()
    {
        var song = new Song { DeezerId = 1, Title = "Space Oddity", Artist = "David Bowie", HasPreview = true };

        var preview = MusicReads.Of(song, 62);
        song.FullContentType = "audio/mp4";
        song.FullSeconds = 318;
        var clip = MusicReads.Of(song, 62);
        var late = MusicReads.Of(song, 400);
        var noStart = MusicReads.Of(song, null);

        await Assert.That(preview).IsEqualTo(new MusicDto(song.Id, "Space Oddity", "David Bowie", null, $"/api/music/songs/{song.Id}/preview", 0, 30));
        await Assert.That(clip!.AudioUrl).EndsWith("/full");
        await Assert.That(clip.Start).IsEqualTo(62);
        await Assert.That(clip.Seconds).IsEqualTo(Song.ClipSeconds);
        await Assert.That(late!.Start).IsEqualTo(318 - Song.ClipSeconds);
        await Assert.That(noStart!.AudioUrl).EndsWith("/preview");
    }

    [Test]
    public async Task APost_PlaysTheSongItWasPostedWith()
    {
        using var h = await FeedHarness.CreateAsync(botCount: 0);
        var store = TestMusic.Store();
        var library = TestMusic.Library(h.Db, store: store, fetcher: new FakeFullSongs(store));
        var song = (await library.AddAsync(1, wantFull: false))!;
        await library.FetchFullAsync(song.Id);

        var result = await new CreatePostHandler(h.Db, h.Notifier, h.Queue, library)
            .Handle(new CreatePostCommand(h.User.Id, "ground control", SongId: song.Id, MusicStart: 62), default);
        var gone = await new CreatePostHandler(h.Db, h.Notifier, h.Queue, library)
            .Handle(new CreatePostCommand(h.User.Id, "major tom", SongId: Guid.NewGuid()), default);

        await Assert.That(result.Value!.Music!.Title).IsEqualTo("Space Oddity");
        await Assert.That(result.Value.Music.Start).IsEqualTo(62);
        await Assert.That(result.Value.Music.AudioUrl).EndsWith("/full");
        await Assert.That(gone.IsFailure).IsTrue();
        var feed = await FeedReads.PostAsync(h.Db, result.Value.Id, h.User.Id);
        await Assert.That(feed!.Music!.SongId).IsEqualTo(song.Id);
    }

    [Test]
    public async Task TheAudio_IsServedFromDisk_ForAStoredSongOnly()
    {
        using var db = TestDbContext.Create();
        var store = TestMusic.Store();
        var song = (await TestMusic.Library(db, store: store).AddAsync(3, wantFull: false))!;
        var handler = new GetSongAudioHandler(db, store);

        var preview = await handler.Handle(new GetSongAudioQuery(song.Id, false), default);
        var full = await handler.Handle(new GetSongAudioQuery(song.Id, true), default);

        await Assert.That(preview.Value).IsEqualTo(new SongAudioFile(store.PathOf(song.Id, false), "audio/mpeg"));
        await Assert.That(full.IsFailure).IsTrue();
    }

    [Test]
    public async Task TheSweep_DeletesSongsNothingUses_AndKeepsTheOthers()
    {
        using var h = await FeedHarness.CreateAsync(botCount: 0);
        var store = TestMusic.Store();
        var library = TestMusic.Library(h.Db, store: store);
        var used = (await library.AddAsync(1, wantFull: false))!;
        var unused = (await library.AddAsync(2, wantFull: false))!;
        var inAStory = (await library.AddAsync(3, wantFull: false))!;
        h.Db.Posts.Add(new Post { AuthorUserId = h.User.Id, Content = "x", SongId = used.Id });
        h.Db.Stories.Add(new Story { AuthorUserId = h.User.Id, Spec = "{}", SizeBytes = 1, SongId = inAStory.Id, ExpiresAt = DateTime.UtcNow.AddDays(5) });
        await h.Db.SaveChangesAsync();

        var swept = await library.SweepAsync(DateTime.UtcNow.AddDays(2));

        await Assert.That(swept).IsEqualTo(1);
        await Assert.That(await h.Db.Songs.Select(s => s.DeezerId).OrderBy(id => id).ToListAsync()).IsEquivalentTo(new long[] { 1, 3 });
        await Assert.That(File.Exists(store.PathOf(unused.Id, false))).IsFalse();
        await Assert.That(File.Exists(store.PathOf(used.Id, false))).IsTrue();
    }

    [Test]
    [Arguments("3", 3, null, null)]
    [Arguments("#2.", 2, null, null)]
    [Arguments("0", 0, null, null)]
    [Arguments("David Bowie - Space Oddity", null, "David Bowie", "Space Oddity")]
    [Arguments("\"Queen - Don't Stop Me Now\"", null, "Queen", "Don't Stop Me Now")]
    [Arguments("hmm, something upbeat", null, null, null)]
    [Arguments("9", null, null, null)]
    public async Task ABotsAnswer_IsANumberFromTheList_OrASongItNamed(string reply, int? index, string? artist, string? title)
    {
        var (i, named) = SongPicker.ParseAnswer(reply, 6);

        await Assert.That(i).IsEqualTo(index);
        await Assert.That(named?.Artist).IsEqualTo(artist);
        await Assert.That(named?.Title).IsEqualTo(title);
    }

    [Test]
    public async Task ANamedSong_CountsOnlyWhenDeezerHasThatVerySong()
    {
        var deezer = new FakeDeezer();

        await Assert.That(SongPicker.Matching(deezer.Tracks, "Bowie", "Space Oddity")!.Id).IsEqualTo(1);
        await Assert.That(SongPicker.Matching(deezer.Tracks, "Elton John", "Tiny Dancer")).IsNull();
    }

    [Test]
    public async Task TheShortlist_MixesSongsForThePostsWordsWithTheChart_EachOnce()
    {
        var deezer = new FakeDeezer();
        using var db = TestDbContext.Create();
        var picker = new SongPicker(deezer, TestMusic.Library(db, deezer), Substitute.For<IOpenRouterService>(), null!,
            Microsoft.Extensions.Options.Options.Create(new MusicOptions { ShortlistSize = 3 }),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SongPicker>.Instance);

        var list = await picker.ShortlistAsync("Ground control to Major Tom, this rocket launch was wild");

        await Assert.That(deezer.Searches.Single()).IsEqualTo("control ground rocket");
        await Assert.That(list.Select(t => t.Id)).IsEquivalentTo(new long[] { 2, 3, 4 });
    }

    [Test]
    public async Task ABot_AddsTheSongItPicksFromTheList_ToItsPost()
    {
        var deezer = new FakeDeezer();
        using var h = await FeedHarness.CreateAsync(botCount: 1, feed: new FeedOptions { ChatInspiredShare = 0, ImageShare = 0 }, music: deezer);
        string? asked = null;
        h.OpenRouter.GenerateResponseAsync(default!, default, default!, default!, default).ReturnsForAnyArgs(ci =>
        {
            var turns = ci.ArgAt<List<ChatMessage>>(3);
            if (turns[^1].Content.Contains("Songs you could add"))
            {
                asked = turns[^1].Content;
                return "2";
            }
            return "off to the moon tonight";
        });

        var post = await h.Scheduler.PostAsync(h.Bots[0], DateTimeOffset.UtcNow, 0.5, musicRoll: 0);
        var silent = await h.Scheduler.PostAsync(h.Bots[0], DateTimeOffset.UtcNow, 0.5, musicRoll: 0.99);

        await Assert.That(asked).Contains("off to the moon tonight");
        // The list is "Fly Me to the Moon" for the post's words, then the chart; the bot took the second.
        await Assert.That(asked).Contains("1. Fly Me to the Moon - Frank Sinatra");
        var song = await h.Db.Songs.SingleAsync(s => s.Id == post!.SongId);
        await Assert.That(song.DeezerId).IsEqualTo(2);
        await Assert.That(post!.MusicStart).IsNull();
        await Assert.That(silent!.SongId).IsNull();
    }

    [Test]
    public async Task ABotNamingASongDeezerDoesntHave_GetsTheFirstOfTheList()
    {
        var deezer = new FakeDeezer();
        using var h = await FeedHarness.CreateAsync(botCount: 1, feed: new FeedOptions { ChatInspiredShare = 0, ImageShare = 0 }, music: deezer);
        h.OpenRouter.GenerateResponseAsync(default!, default, default!, default!, default).ReturnsForAnyArgs(ci =>
            ci.ArgAt<List<ChatMessage>>(3)[^1].Content.Contains("Songs you could add") ? "Made Up Band - Not A Real Song" : "quiet sunday");

        var post = await h.Scheduler.PostAsync(h.Bots[0], DateTimeOffset.UtcNow, 0.5, musicRoll: 0);

        var song = await h.Db.Songs.SingleAsync(s => s.Id == post!.SongId);
        await Assert.That(song.DeezerId).IsEqualTo(deezer.Chart[0]);
    }
}
