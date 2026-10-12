using Microsoft.EntityFrameworkCore;
using NeuralDamage.Domain;
using NeuralDamage.Infrastructure.Dtos;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.Stickers;
using NeuralDamage.Tests.Helpers;
using NSubstitute;

namespace NeuralDamage.Tests.Infrastructure;

/// <summary>How bots know the stickers, send them, and see the ones others send.</summary>
public class StickerPromptTests
{
    private static Sticker Sticker(string name, string? description = "something", int uses = 0, DateTime? at = null, bool deleted = false) => new()
    {
        Name = name,
        Description = description,
        ContentType = "image/png",
        SizeBytes = 10,
        UploaderUserId = Guid.NewGuid(),
        UseCount = uses,
        CreatedAt = at ?? DateTime.UtcNow,
        DeletedAt = deleted ? DateTime.UtcNow : null,
    };

    [Test]
    public async Task PromptList_IsCapped_MostUsedThenNewest_ListedByName()
    {
        var now = DateTime.UtcNow;
        var stickers = Enumerable.Range(0, 60)
            .Select(i => Sticker($"s{i:00}", uses: i < 5 ? 100 : 0, at: now.AddMinutes(i)))
            .Append(Sticker("undescribed", description: null, uses: 1000))
            .Append(Sticker("deleted", uses: 1000, deleted: true))
            .ToList();

        var forPrompt = new StickerCatalog(stickers).ForPrompt;

        await Assert.That(forPrompt.Count).IsEqualTo(StickerCatalog.PromptLimit);
        var names = forPrompt.Select(s => s.Name).ToList();
        // The five most used, then the 35 newest.
        await Assert.That(names.Take(5)).IsEquivalentTo(["s00", "s01", "s02", "s03", "s04"]);
        await Assert.That(names).Contains("s59");
        await Assert.That(names).DoesNotContain("s05");
        await Assert.That(names).DoesNotContain("undescribed");
        await Assert.That(names).DoesNotContain("deleted");
        await Assert.That(names).IsEquivalentTo(names.Order(StringComparer.Ordinal).ToList());

        // The same library in another order reads the same, so the prompt keeps hitting the cache.
        var shuffled = new StickerCatalog(stickers.AsEnumerable().Reverse()).ForPrompt;
        await Assert.That(shuffled.Select(s => s.Name)).IsEquivalentTo(names);
    }

    [Test]
    public async Task ABigLibrary_StillPutsOnlyFortyIntoThePrompt()
    {
        using var db = TestDbContext.Create();
        db.Stickers.AddRange(Enumerable.Range(0, 5000).Select(i => Sticker($"s{i:0000}", $"sticker number {i}", uses: i % 7)));
        await db.SaveChangesAsync();

        var catalog = await StickerCatalog.LoadAsync(db);
        var bot = new Bot { Name = "Gus", ModelId = "m", CreatedById = Guid.NewGuid() };
        var prompt = BotPromptBuilder.WithStickers(BotPromptBuilder.BuildSystemPrompt(bot, ["Alice", "Gus"]), catalog.ForPrompt);

        await Assert.That(catalog.ForPrompt.Count).IsEqualTo(40);
        await Assert.That(prompt.Split('\n').Count(l => l.Contains(": sticker number "))).IsEqualTo(40);
        // Every one of them can still be sent by name.
        await Assert.That(catalog.Find("s4999")?.Name).IsEqualTo("s4999");
    }

    [Test]
    public async Task ImageDescriber_NamesWhoItRecognises_WithoutMakingNamesUp()
    {
        var prompt = NeuralDamage.Infrastructure.Services.Attachments.ImageDescriber.Prompt;

        await Assert.That(prompt).DoesNotContain("never guess who they are");
        await Assert.That(prompt).Contains("Name the person, character or meme when you recognise them");
        await Assert.That(prompt).Contains("never make up a name");
        await Assert.That(prompt).Contains("Copy any visible text word for word.");
    }

    [Test]
    public async Task StickerDescriber_NamesWhoItRecognises_WithoutMakingNamesUp()
    {
        var prompt = NeuralDamage.Infrastructure.Services.Attachments.ImageDescriber.StickerPrompt;

        await Assert.That(prompt).DoesNotContain("Never guess who a person is");
        await Assert.That(prompt).Contains("Name the person, character or meme when you recognise them");
        await Assert.That(prompt).Contains("distracted boyfriend");
        await Assert.That(prompt).Contains("never make up a name");
        // The rest stays.
        await Assert.That(prompt).Contains("Copy any visible text word for word.");
        await Assert.That(prompt).Contains("at most 20 words");
    }

    [Test]
    public async Task SystemPrompt_ListsTheStickers_OnlyWhenThereAreAny()
    {
        var bot = new Bot { Name = "Gus", ModelId = "m", CreatedById = Guid.NewGuid() };
        var basePrompt = BotPromptBuilder.BuildSystemPrompt(bot, ["Alice", "Gus"]);
        var catalog = new StickerCatalog([Sticker("cat-lol", "cat crying with laughter, for when something is hilarious"), Sticker("oops", "monkey covering its eyes, for cringe moments")]);

        var with = BotPromptBuilder.WithStickers(basePrompt, catalog.ForPrompt);
        var without = BotPromptBuilder.WithStickers(basePrompt, StickerCatalog.Empty.ForPrompt);

        await Assert.That(with).StartsWith(basePrompt.TrimEnd());
        await Assert.That(with).Contains("cat-lol: cat crying with laughter, for when something is hilarious\noops: monkey covering its eyes, for cringe moments");
        await Assert.That(with).Contains("[sticker: name]");
        await Assert.That(without).IsEqualTo(basePrompt);
        await Assert.That(without).DoesNotContain("[sticker");
    }

    [Test]
    public async Task Find_ForgivesCaseAndSpacing_AndKnowsOnlyDescribedStickers()
    {
        var catalog = new StickerCatalog([Sticker("cat-lol"), Sticker("blank", description: " ")]);

        await Assert.That(catalog.Find("Cat LOL")?.Name).IsEqualTo("cat-lol");
        await Assert.That(catalog.Find("blank")).IsNull();
        await Assert.That(catalog.Find("dog")).IsNull();
    }

    [Test]
    public async Task ExtractStickers_KnownBecomeMessages_UnknownVanish_TextStays()
    {
        var catalog = new StickerCatalog([Sticker("cat-lol")]);

        var parts = BotReplyFormatter.ExtractStickers(["no way\n[sticker: cat-lol]", "[sticker: unicorn]", "lol [sticker: Cat Lol] same"], catalog.Find);

        await Assert.That(parts.Select(p => p.Sticker?.Name ?? p.Text)).IsEquivalentTo(["no way", "cat-lol", "lol", "cat-lol", "same"]);
        await Assert.That(parts.Where(p => p.Sticker is not null).All(p => p.Text == "")).IsTrue();
    }

    [Test]
    public async Task HistoryLines_ShowWhoSentWhichSticker()
    {
        var alice = new User { ExternalId = "a", Email = "a@test.com", DisplayName = "Alice" };
        var gus = new Bot { Name = "Gus", ModelId = "m", CreatedById = alice.Id };
        var cat = Sticker("cat-lol", "cat crying with laughter");
        var fromAlice = new Message { ChatId = Guid.NewGuid(), SenderUserId = alice.Id, SenderUser = alice, Content = "", Sticker = cat, StickerId = cat.Id };
        var fromGus = new Message { ChatId = fromAlice.ChatId, SenderBotId = gus.Id, SenderBot = gus, Content = "", Sticker = cat, StickerId = cat.Id, CreatedAt = fromAlice.CreatedAt.AddSeconds(1) };
        var undescribed = new Message { ChatId = fromAlice.ChatId, SenderUserId = alice.Id, SenderUser = alice, Content = "", Sticker = Sticker("new", null), CreatedAt = fromAlice.CreatedAt.AddSeconds(2) };

        await Assert.That(BotPromptBuilder.WithImages(fromAlice)).IsEqualTo("[sticker from Alice: cat crying with laughter]");
        await Assert.That(BotPromptBuilder.WithImages(undescribed)).IsEqualTo("[sticker from Alice]");

        var forGus = BotPromptBuilder.BuildHistory([fromAlice, fromGus, undescribed], gus.Id);
        await Assert.That(forGus[0].Content).IsEqualTo("[Alice]: [sticker from Alice: cat crying with laughter]");
        // Its own sticker reads the way it sends one.
        await Assert.That(forGus[1].Role).IsEqualTo("assistant");
        await Assert.That(forGus[1].Content).IsEqualTo("[sticker: cat-lol]");
    }

    [Test]
    public async Task ImitatedStickerLines_AreDropped()
    {
        await Assert.That(BotReplyFormatter.StripImageLines("haha\n[sticker from Alice: a cat]")).IsEqualTo("haha");
    }

    [Test]
    public async Task BotReply_WithASticker_PostsItAsItsOwnMessage_AndCountsTheUse()
    {
        using var h = await OrchestratorHarness.CreateAsync();
        var gpt = h.Bots[0];
        var cat = Sticker("cat-lol", "cat crying with laughter");
        h.Db.Stickers.Add(cat);
        await h.Db.SaveChangesAsync();
        h.Respond(gpt);
        h.Reply("no way\n[sticker: cat-lol]\n\n[sticker: does-not-exist]");
        var trigger = await h.SayAsync("I fell off my chair");

        await h.Orchestrator.ProcessMessageAsync(h.Chat.Id, trigger.Id);

        var sent = await h.MessagesFromAsync(gpt);
        await Assert.That(sent.Count).IsEqualTo(2);
        await Assert.That(sent[0].Content).IsEqualTo("no way");
        await Assert.That(sent[1].Content).IsEqualTo("");
        await Assert.That(sent[1].StickerId).IsEqualTo(cat.Id);
        await h.Notifications.Received(1).NotifyMessageNew(h.Chat.Id, Arg.Is<MessageDto>(m => m.Sticker != null && m.Sticker.Name == "cat-lol"));
        var stored = await h.Db.Stickers.AsNoTracking().SingleAsync();
        await Assert.That(stored.UseCount).IsEqualTo(1);
        await h.OpenRouter.Received().GenerateResponseAsync(Arg.Any<string>(), Arg.Any<double>(),
            Arg.Is<string>(p => p.Contains("cat-lol: cat crying with laughter")), Arg.Any<List<ChatMessage>>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task BotReply_OfOnlyAnUnknownSticker_PostsNothing()
    {
        using var h = await OrchestratorHarness.CreateAsync();
        var gpt = h.Bots[0];
        h.Respond(gpt);
        h.Reply("[sticker: ghost]");
        var trigger = await h.SayAsync("hello");

        await h.Orchestrator.ProcessMessageAsync(h.Chat.Id, trigger.Id);

        await Assert.That(await h.MessagesFromAsync(gpt)).IsEmpty();
    }
}
