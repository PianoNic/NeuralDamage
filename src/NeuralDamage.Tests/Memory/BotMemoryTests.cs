using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NeuralDamage.Application.Commands;
using NeuralDamage.Domain;
using NeuralDamage.Domain.Enums;
using NeuralDamage.Infrastructure;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.Memory;
using NeuralDamage.Tests.Helpers;
using NSubstitute;

namespace NeuralDamage.Tests.Memory;

/// <summary>One person, a friend, a bot, a group chat with all three and a direct chat with the bot.</summary>
internal sealed class MemoryWorld : IDisposable
{
    public required NeuralDamageDbContext Db { get; init; }
    public required User Me { get; init; }
    public required User Friend { get; init; }
    public required Bot Bot { get; init; }
    public required Chat Group { get; init; }
    public required Chat Direct { get; init; }

    public static async Task<MemoryWorld> CreateAsync(NeuralDamageDbContext? db = null)
    {
        db ??= TestDbContext.Create();
        var me = new User { ExternalId = "ext-1", Email = "me@example.com", DisplayName = "PianoNic", Nicknames = "Niclas" };
        var friend = new User { ExternalId = "ext-2", Email = "friend@example.com", DisplayName = "Bob" };
        db.Users.AddRange(me, friend);
        var bot = new Bot { Name = "Rex", ModelId = "m", SystemPrompt = "x", CreatedById = me.Id };
        db.Bots.Add(bot);
        var group = new Chat { Name = "Group", CreatedById = me.Id };
        var direct = new Chat { Name = "Rex", CreatedById = me.Id, DirectKey = Chat.DirectKeyOf(me.Id, bot.Id) };
        db.Chats.AddRange(group, direct);
        db.ChatMembers.AddRange(
            new ChatMember { ChatId = group.Id, UserId = me.Id, Role = ChatMemberRole.Owner },
            new ChatMember { ChatId = group.Id, UserId = friend.Id },
            new ChatMember { ChatId = group.Id, BotId = bot.Id },
            new ChatMember { ChatId = direct.Id, UserId = me.Id, Role = ChatMemberRole.Owner },
            new ChatMember { ChatId = direct.Id, BotId = bot.Id });
        await db.SaveChangesAsync();
        return new MemoryWorld { Db = db, Me = me, Friend = friend, Bot = bot, Group = group, Direct = direct };
    }

    public BotMemory About(User user, string text, Chat? learnedIn = null, bool privately = false) =>
        new() { BotId = Bot.Id, Scope = MemoryScope.Person, SubjectUserId = user.Id, ChatId = learnedIn?.Id, StaysInChat = privately, Text = text };

    public async Task AddAsync(params BotMemory[] memories)
    {
        Db.BotMemories.AddRange(memories);
        await Db.SaveChangesAsync();
    }

    public void Dispose() => Db.Dispose();
}

public class BotMemoryRecallTests
{
    private static readonly MemoryOptions Options = new();

    [Test]
    public async Task WhatIsBeingTalkedAbout_ComesFirst()
    {
        using var w = await MemoryWorld.CreateAsync();
        await w.AddAsync(
            new BotMemory { BotId = w.Bot.Id, Scope = MemoryScope.Self, Text = "Rex hates rainy mondays" },
            new BotMemory { BotId = w.Bot.Id, Scope = MemoryScope.Self, Text = "Rex makes the best pizza dough in town" });

        var recalled = await BotMemories.RecallForChatAsync(w.Db, w.Bot, w.Group.Id, "anyone want pizza tonight?", new MemoryOptions { MaxRecalled = 1 });

        await Assert.That(recalled.Single().Text).IsEqualTo("Rex makes the best pizza dough in town");
    }

    [Test]
    public async Task WhatWasSaidInPrivate_StaysThere()
    {
        using var w = await MemoryWorld.CreateAsync();
        await w.AddAsync(
            w.About(w.Me, "PianoNic told Rex a secret about his job", w.Direct, privately: true),
            w.About(w.Me, "PianoNic plays the piano", w.Group));

        var inGroup = await BotMemories.RecallForChatAsync(w.Db, w.Bot, w.Group.Id, "", Options);
        var inDirect = await BotMemories.RecallForChatAsync(w.Db, w.Bot, w.Direct.Id, "", Options);

        await Assert.That(inGroup.Select(m => m.Text)).IsEquivalentTo(new[] { "PianoNic plays the piano" });
        await Assert.That(inDirect).Count().IsEqualTo(2);
    }

    [Test]
    public async Task AChatsLore_OnlyComesUpInThatChat()
    {
        using var w = await MemoryWorld.CreateAsync();
        await w.AddAsync(new BotMemory { BotId = w.Bot.Id, Scope = MemoryScope.Chat, ChatId = w.Group.Id, StaysInChat = true, Text = "The group argues about pineapple pizza" });

        await Assert.That(await BotMemories.RecallForChatAsync(w.Db, w.Bot, w.Group.Id, "", Options)).Count().IsEqualTo(1);
        await Assert.That(await BotMemories.RecallForChatAsync(w.Db, w.Bot, w.Direct.Id, "", Options)).IsEmpty();
    }

    [Test]
    public async Task SomeoneWhoTurnedMemoryOff_IsNeverRecalled()
    {
        using var w = await MemoryWorld.CreateAsync();
        await w.AddAsync(w.About(w.Friend, "Bob has a dog", w.Group));
        w.Friend.AllowBotMemory = false;
        await w.Db.SaveChangesAsync();

        await Assert.That(await BotMemories.RecallForChatAsync(w.Db, w.Bot, w.Group.Id, "dog", Options)).IsEmpty();
    }

    [Test]
    public async Task TheFeed_OnlyGetsWhatTheBotKnowsAboutItself()
    {
        using var w = await MemoryWorld.CreateAsync();
        await w.AddAsync(
            new BotMemory { BotId = w.Bot.Id, Scope = MemoryScope.Self, Text = "You posted on the feed: pizza night!" },
            new BotMemory { BotId = w.Bot.Id, Scope = MemoryScope.Self, ChatId = w.Direct.Id, StaysInChat = true, Text = "Rex promised to keep a secret" },
            w.About(w.Me, "PianoNic plays the piano", w.Group));

        var self = await BotMemories.RecallSelfAsync(w.Db, w.Bot, "", Options);

        await Assert.That(self.Select(m => m.Text)).IsEquivalentTo(new[] { "You posted on the feed: pizza night!" });
    }

    [Test]
    public async Task Recall_KeepsToTheLimits_AndMarksWhatItUsed()
    {
        using var w = await MemoryWorld.CreateAsync();
        await w.AddAsync(Enumerable.Range(0, 20).Select(i => new BotMemory { BotId = w.Bot.Id, Scope = MemoryScope.Self, Text = $"Rex remembers thing number {i} quite well" }).ToArray());

        var recalled = await BotMemories.RecallForChatAsync(w.Db, w.Bot, w.Group.Id, "", new MemoryOptions { MaxRecalled = 5 });

        await Assert.That(recalled).Count().IsEqualTo(5);
        await Assert.That(await w.Db.BotMemories.CountAsync(m => m.LastUsedAt != null)).IsEqualTo(5);
    }

    [Test]
    public async Task ABotWithMemoryOff_RecallsNothing()
    {
        using var w = await MemoryWorld.CreateAsync();
        await w.AddAsync(new BotMemory { BotId = w.Bot.Id, Scope = MemoryScope.Self, Text = "Rex likes jazz" });
        w.Bot.MemoryEnabled = false;

        await Assert.That(await BotMemories.RecallForChatAsync(w.Db, w.Bot, w.Group.Id, "jazz", Options)).IsEmpty();
    }

    [Test]
    public async Task TheNote_GroupsByPerson_AfterTheMakersNotes()
    {
        var bot = new Bot { Name = "Rex", ModelId = "m", SystemPrompt = "x", CreatedById = Guid.NewGuid(), MemoryNotes = "Rex is from Bern." };

        var note = BotMemories.Note(bot, [
            new RecalledMemory(Guid.NewGuid(), MemoryScope.Person, "PianoNic has a cat called Olivia", "PianoNic"),
            new RecalledMemory(Guid.NewGuid(), MemoryScope.Self, "Rex promised a song", null),
        ])!;

        await Assert.That(note).Contains("Rex is from Bern.");
        await Assert.That(note).Contains("About PianoNic: PianoNic has a cat called Olivia.");
        await Assert.That(note).Contains("Rex promised a song.");
        bot.MemoryNotes = null;
        await Assert.That(BotMemories.Note(bot, [])).IsNull();
    }
}

public class BotMemoryWritingTests
{
    [Test]
    public async Task SayingTheSameThingTwice_KeepsOneMemory()
    {
        using var w = await MemoryWorld.CreateAsync();

        var first = await BotMemories.AddAsync(w.Db, w.About(w.Me, "PianoNic has a cat called Olivia", w.Group));
        var again = await BotMemories.AddAsync(w.Db, w.About(w.Me, "pianonic has a cat called olivia.", w.Group));
        var other = await BotMemories.AddAsync(w.Db, w.About(w.Me, "PianoNic is learning Japanese", w.Group));

        await Assert.That(first).IsTrue();
        await Assert.That(again).IsFalse();
        await Assert.That(other).IsTrue();
        await Assert.That(await w.Db.BotMemories.CountAsync()).IsEqualTo(2);
    }

    [Test]
    public async Task ABotsOwnPost_IsRememberedWithoutAModel()
    {
        using var w = await MemoryWorld.CreateAsync();

        await BotMemories.RememberOwnPostAsync(w.Db, w.Bot, MemorySource.Feed, "pizza night at mine!", new MemoryOptions());

        var memory = await w.Db.BotMemories.SingleAsync();
        await Assert.That(memory.Scope).IsEqualTo(MemoryScope.Self);
        await Assert.That(memory.Text).IsEqualTo("You posted on the feed: pizza night at mine!");
    }

    [Test]
    public async Task Pruning_DropsTheLeastUseful_NeverPinnedOrWrittenIn()
    {
        using var w = await MemoryWorld.CreateAsync();
        var pinned = new BotMemory { BotId = w.Bot.Id, Scope = MemoryScope.Self, Text = "Pinned forever", Pinned = true, Importance = 1 };
        var manual = new BotMemory { BotId = w.Bot.Id, Scope = MemoryScope.Chat, ChatId = w.Group.Id, Source = MemorySource.Manual, Text = "Typed in", Importance = 1 };
        await w.AddAsync([pinned, manual, .. Enumerable.Range(0, 10).Select(i => new BotMemory { BotId = w.Bot.Id, Scope = MemoryScope.Self, Text = $"Thing {i} that matters", Importance = i + 1 })]);

        var dropped = await BotMemories.PruneAsync(w.Db, w.Bot.Id, new MemoryOptions { MaxPerBot = 5 });

        await Assert.That(dropped).IsEqualTo(7);
        await Assert.That(await w.Db.BotMemories.AnyAsync(m => m.Id == pinned.Id)).IsTrue();
        await Assert.That(await w.Db.BotMemories.AnyAsync(m => m.Id == manual.Id)).IsTrue();
        await Assert.That(await w.Db.BotMemories.AnyAsync(m => m.Text == "Thing 9 that matters")).IsTrue();
    }
}

public class MemoryExtractorTests
{
    [Test]
    public async Task TheModelsAnswer_IsReadEvenWithChatterAroundIt()
    {
        var found = MemoryExtractor.Parse("""
            Sure! Here you go:
            [{"about": "Niclas", "text": "Niclas has a cat called Olivia", "importance": 7}, {"about": "self", "text": "Rex promised to bake"}, 3, {"text": ""}]
            """);

        await Assert.That(found).Count().IsEqualTo(2);
        await Assert.That(found[0]).IsEqualTo(new MemoryExtractor.Found("Niclas", "Niclas has a cat called Olivia", 7));
        await Assert.That(found[1].Importance).IsEqualTo(5);
        await Assert.That(MemoryExtractor.Parse("no json here")).IsEmpty();
        await Assert.That(MemoryExtractor.Parse("[{broken")).IsEmpty();
    }

    [Test]
    public async Task APersonIsFoundByNickname_AndWhatADirectChatTeachesStaysInIt()
    {
        using var w = await MemoryWorld.CreateAsync();
        var people = new List<User> { w.Me, w.Friend };

        var inGroup = MemoryExtractor.ToMemory(new("Niclas", "Niclas has a cat", 6), w.Bot, w.Group, people, inPrivate: false)!;
        var inDirect = MemoryExtractor.ToMemory(new("PianoNic", "PianoNic is tired", 4), w.Bot, w.Direct, people, inPrivate: true)!;
        var stranger = MemoryExtractor.ToMemory(new("Carol", "Carol likes tea", 4), w.Bot, w.Group, people, inPrivate: false);

        await Assert.That(inGroup.SubjectUserId).IsEqualTo(w.Me.Id);
        await Assert.That(inGroup.StaysInChat).IsFalse();
        await Assert.That(inDirect.StaysInChat).IsTrue();
        await Assert.That(inDirect.ChatId).IsEqualTo(w.Direct.Id);
        await Assert.That(stranger).IsNull();
    }

    [Test]
    public async Task SomeoneWhoTurnedMemoryOff_IsNotWrittenDown()
    {
        using var w = await MemoryWorld.CreateAsync();
        w.Friend.AllowBotMemory = false;

        await Assert.That(MemoryExtractor.ToMemory(new("Bob", "Bob has a dog", 5), w.Bot, w.Group, [w.Me, w.Friend], false)).IsNull();
    }

    [Test]
    public async Task AChat_IsReadOnceTheBotHasSaidEnough_AndOnlyFromWhereItLeftOff()
    {
        var dbOptions = new DbContextOptionsBuilder<NeuralDamageDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        using var w = await MemoryWorld.CreateAsync(new NeuralDamageDbContext(dbOptions));
        var openRouter = Substitute.For<IOpenRouterService>();
        openRouter.GenerateResponseAsync(default!, default, default!, default!, default).ReturnsForAnyArgs(
            """[{"about": "Niclas", "text": "Niclas has a cat called Olivia", "importance": 7}]""");
        var services = new ServiceCollection();
        services.AddScoped(_ => new NeuralDamageDbContext(dbOptions));
        services.AddSingleton(openRouter);
        using var provider = services.BuildServiceProvider();
        var extractor = new MemoryExtractor(provider.GetRequiredService<IServiceScopeFactory>(), new ConfigurationBuilder().Build(),
            NullLogger<MemoryExtractor>.Instance, Options.Create(new MemoryOptions { ExtractEvery = 2 }));

        var start = DateTime.UtcNow.AddMinutes(-10);
        w.Db.Messages.Add(new Message { ChatId = w.Group.Id, SenderUserId = w.Me.Id, Content = "my cat Olivia knocked over my coffee", CreatedAt = start });
        w.Db.Messages.Add(new Message { ChatId = w.Group.Id, SenderBotId = w.Bot.Id, Content = "classic Olivia", CreatedAt = start.AddMinutes(1) });
        await w.Db.SaveChangesAsync();
        var tooSoon = await extractor.ReadAsync(w.Group.Id, w.Bot.Id);

        w.Db.Messages.Add(new Message { ChatId = w.Group.Id, SenderBotId = w.Bot.Id, Content = "cats are chaos", CreatedAt = start.AddMinutes(2) });
        await w.Db.SaveChangesAsync();
        var read = await extractor.ReadAsync(w.Group.Id, w.Bot.Id);
        var again = await extractor.ReadAsync(w.Group.Id, w.Bot.Id);

        await Assert.That(tooSoon).IsEqualTo(0);
        await Assert.That(read).IsEqualTo(1);
        await Assert.That(again).IsEqualTo(0);
        await openRouter.ReceivedWithAnyArgs(1).GenerateResponseAsync(default!, default, default!, default!, default);
        await using var check = new NeuralDamageDbContext(dbOptions);
        var memory = await check.BotMemories.SingleAsync();
        await Assert.That(memory.SubjectUserId).IsEqualTo(w.Me.Id);
        await Assert.That(memory.Text).IsEqualTo("Niclas has a cat called Olivia");
    }
}

public class BotMemoryAccessTests
{
    [Test]
    public async Task TheMaker_DoesNotReadWhatSomeoneElseToldTheBotInPrivate()
    {
        using var w = await MemoryWorld.CreateAsync();
        var bobsDirect = new Chat { Name = "Rex", CreatedById = w.Friend.Id, DirectKey = Chat.DirectKeyOf(w.Friend.Id, w.Bot.Id) };
        w.Db.Chats.Add(bobsDirect);
        w.Db.ChatMembers.Add(new ChatMember { ChatId = bobsDirect.Id, UserId = w.Friend.Id, Role = ChatMemberRole.Owner });
        await w.AddAsync(
            w.About(w.Friend, "Bob is nervous about his exam", bobsDirect, privately: true),
            w.About(w.Friend, "Bob likes football", w.Group),
            new BotMemory { BotId = w.Bot.Id, Scope = MemoryScope.Self, Text = "Rex supports Young Boys" });

        var maker = await new GetBotMemoriesHandler(w.Db).Handle(new GetBotMemoriesQuery(w.Bot.Id, w.Me.Id), default);
        var bobs = await new GetMemoriesAboutMeHandler(w.Db).Handle(new GetMemoriesAboutMeQuery(w.Friend.Id), default);

        await Assert.That(maker.Value!.Memories.Select(m => m.Text)).IsEquivalentTo(new[] { "Bob likes football", "Rex supports Young Boys" });
        await Assert.That(maker.Value.Hidden).IsEqualTo(1);
        await Assert.That(bobs.Value!.Select(m => m.Text)).IsEquivalentTo(new[] { "Bob is nervous about his exam", "Bob likes football" });
        await Assert.That((await new DeleteBotMemoryHandler(w.Db).Handle(new DeleteBotMemoryCommand(w.Db.BotMemories.Single(m => m.StaysInChat).Id, w.Me.Id), default)).IsSuccess).IsFalse();
    }

    [Test]
    public async Task OnlyTheMaker_SeesOrClearsABotsMemory()
    {
        using var w = await MemoryWorld.CreateAsync();
        await w.AddAsync(new BotMemory { BotId = w.Bot.Id, Scope = MemoryScope.Self, Text = "Rex likes jazz" });

        var stranger = await new GetBotMemoriesHandler(w.Db).Handle(new GetBotMemoriesQuery(w.Bot.Id, w.Friend.Id), default);
        var cleared = await new ClearBotMemoryHandler(w.Db).Handle(new ClearBotMemoryCommand(w.Bot.Id, w.Me.Id), default);

        await Assert.That(stranger.IsSuccess).IsFalse();
        await Assert.That(cleared.Value).IsEqualTo(1);
        await Assert.That(await w.Db.BotMemories.AnyAsync()).IsFalse();
    }

    [Test]
    public async Task TurningMemoryOff_ForgetsWhatBotsKnewAboutYou()
    {
        using var w = await MemoryWorld.CreateAsync();
        await w.AddAsync(w.About(w.Me, "PianoNic plays the piano", w.Group), w.About(w.Friend, "Bob likes football", w.Group));

        var result = await new AllowBotMemoryHandler(w.Db, AppAdmins.None).Handle(new AllowBotMemoryCommand(w.Me.Id, false), default);

        await Assert.That(result.Value!.AllowBotMemory).IsFalse();
        await Assert.That(w.Db.BotMemories.Select(m => m.Text)).IsEquivalentTo(new[] { "Bob likes football" });
    }

    [Test]
    public async Task TheMaker_PinsAndRewordsAMemory_AndSetsTheNotes()
    {
        using var w = await MemoryWorld.CreateAsync();
        var memory = new BotMemory { BotId = w.Bot.Id, Scope = MemoryScope.Self, Text = "Rex likes jaz" };
        await w.AddAsync(memory);

        var updated = await new UpdateBotMemoryHandler(w.Db).Handle(new UpdateBotMemoryCommand(w.Bot.Id, memory.Id, w.Me.Id, "Rex likes jazz", true), default);
        var settings = await new UpdateBotMemorySettingsHandler(w.Db).Handle(new UpdateBotMemorySettingsCommand(w.Bot.Id, w.Me.Id, false, "  Rex is from Bern. "), default);

        await Assert.That(updated.Value!.Text).IsEqualTo("Rex likes jazz");
        await Assert.That(updated.Value.Pinned).IsTrue();
        await Assert.That(settings.IsSuccess).IsTrue();
        await Assert.That(w.Bot.MemoryEnabled).IsFalse();
        await Assert.That(w.Bot.MemoryNotes).IsEqualTo("Rex is from Bern.");
    }
}

public class MemoryInRepliesTests
{
    [Test]
    public async Task ABotsReply_IsWrittenWithWhatItRemembers_AndItsMemoryHearsItSpoke()
    {
        var extractor = Substitute.For<IMemoryExtractor>();
        using var h = await OrchestratorHarness.CreateAsync(configure: s => s.AddSingleton(extractor));
        var bot = h.Bots[0];
        h.Db.BotMemories.Add(new BotMemory { BotId = bot.Id, Scope = MemoryScope.Person, SubjectUserId = h.User.Id, ChatId = h.Chat.Id, Text = "Alice has a cat called Olivia" });
        await h.Db.SaveChangesAsync();
        List<ChatMessage>? history = null;
        h.OpenRouter.GenerateResponseAsync(Arg.Any<string>(), Arg.Any<double>(), Arg.Any<string>(), Arg.Do<List<ChatMessage>>(x => history = x), Arg.Any<CancellationToken>())
            .Returns("how is Olivia?");
        h.Respond(bot);

        var message = await h.SayAsync("my cat is being weird today");
        await h.Orchestrator.ProcessMessageAsync(h.Chat.Id, message.Id);

        await Assert.That(history!.Any(m => m.Content.Contains("About Alice: Alice has a cat called Olivia."))).IsTrue();
        await extractor.Received(1).BotSpoke(h.Chat.Id, bot.Id);
    }
}
