using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NeuralDamage.Application.Commands;
using NeuralDamage.Application.Queries;
using NeuralDamage.Domain;
using NeuralDamage.Infrastructure.Dtos;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.BotDecision;
using NeuralDamage.Infrastructure.Services.Feed;
using NeuralDamage.Tests.Helpers;
using NSubstitute;

namespace NeuralDamage.Tests.Feed;

/// <summary>When bots post on their own: caps, their waking hours in the app's time zone, the switch.</summary>
public class FeedScheduleTests
{
    private static readonly BotClock Zurich = new(BotClock.Resolve("Europe/Zurich"));

    private static Bot Bot(int cap = 3, bool feed = true, bool isPublic = true) =>
        new() { Name = "Rex", ModelId = "m", CreatedById = Guid.NewGuid(), MaxPostsPerDay = cap, FeedEnabled = feed, IsPublic = isPublic };

    /// <summary>Noon in Zurich (10:00 UTC in summer).</summary>
    private static readonly DateTimeOffset Noon = new(2026, 7, 15, 10, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task ABotUnderItsCap_InItsWakingHours_PostsWhenTheDiceSaySo()
    {
        await Assert.That(FeedSchedule.IsDue(Bot(), 0, null, Noon, Zurich, new FeedOptions(), roll: 0)).IsTrue();
    }

    [Test]
    public async Task AtItsCap_ABotNeverPosts()
    {
        await Assert.That(FeedSchedule.IsDue(Bot(cap: 2), 2, null, Noon, Zurich, new FeedOptions(), roll: 0)).IsFalse();
        await Assert.That(FeedSchedule.IsDue(Bot(cap: 0), 0, null, Noon, Zurich, new FeedOptions(), roll: 0)).IsFalse();
    }

    [Test]
    public async Task AtNightInTheAppsTimeZone_NobodyPosts_EvenWhenItIsDayInUtc()
    {
        // 23:30 in Zurich is 21:30 UTC: still evening in UTC, already night in the app.
        var lateInZurich = new DateTimeOffset(2026, 7, 15, 21, 30, 0, TimeSpan.Zero);
        await Assert.That(FeedSchedule.IsDue(Bot(), 0, null, lateInZurich, Zurich, new FeedOptions(), roll: 0)).IsFalse();
        await Assert.That(FeedSchedule.IsDue(Bot(), 0, null, lateInZurich, new BotClock(TimeZoneInfo.Utc), new FeedOptions(), roll: 0)).IsTrue();

        var earlyMorning = new DateTimeOffset(2026, 7, 15, 4, 0, 0, TimeSpan.Zero); // 06:00 in Zurich
        await Assert.That(FeedSchedule.IsDue(Bot(), 0, null, earlyMorning, Zurich, new FeedOptions(), roll: 0)).IsFalse();
    }

    [Test]
    public async Task TheSwitchOff_OrAPrivateBot_NeverPosts()
    {
        await Assert.That(FeedSchedule.IsDue(Bot(feed: false), 0, null, Noon, Zurich, new FeedOptions(), roll: 0)).IsFalse();
        await Assert.That(FeedSchedule.IsDue(Bot(isPublic: false), 0, null, Noon, Zurich, new FeedOptions(), roll: 0)).IsFalse();
    }

    [Test]
    public async Task RightAfterAPost_ABotWaitsBeforeTheNext()
    {
        var justNow = Noon.UtcDateTime.AddMinutes(-20);
        await Assert.That(FeedSchedule.IsDue(Bot(cap: 3), 1, justNow, Noon, Zurich, new FeedOptions(), roll: 0)).IsFalse();
        var hoursAgo = Noon.UtcDateTime.AddHours(-5);
        await Assert.That(FeedSchedule.IsDue(Bot(cap: 3), 1, hoursAgo, Noon, Zurich, new FeedOptions(), roll: 0)).IsTrue();
    }

    [Test]
    public async Task ADayStartsAtLocalMidnight()
    {
        var start = FeedSchedule.StartOfDay(Noon, Zurich);
        await Assert.That(start).IsEqualTo(new DateTime(2026, 7, 14, 22, 0, 0, DateTimeKind.Utc));
    }

    [Test]
    public async Task TheScheduler_PostsWithTheDefaultModel_AndKeepsToTheCapAndTheSwitch()
    {
        var time = new FixedTime(new DateTimeOffset(2026, 7, 15, 13, 0, 0, TimeSpan.Zero));
        using var h = await FeedHarness.CreateAsync(botCount: 3, feed: new FeedOptions { MaxPostsPerTick = 10, ChatInspiredShare = 0, StoryShare = 0 }, time: time);
        h.Bots[1].FeedEnabled = false;
        h.Bots[2].MaxPostsPerDay = 1;
        await h.Db.SaveChangesAsync();
        await h.PostAsync("already posted today", h.Bots[2], at: time.Now.UtcDateTime.AddHours(-6));
        h.Reply("kitchen's closed, donkeys");

        var posted = (await h.Scheduler.TickAsync(random: () => 0)).Posts;

        await Assert.That(posted.Select(p => p.AuthorBotId)).IsEquivalentTo(new Guid?[] { h.Bots[0].Id });
        await h.OpenRouter.Received(1).GenerateResponseAsync("gateway:free", Arg.Any<double>(), Arg.Any<string>(), Arg.Any<List<ChatMessage>>(), Arg.Any<CancellationToken>());
        await h.Notifier.Received(1).PostNew(Arg.Is<PostDto>(p => p.Content == "kitchen's closed, donkeys"));
        await Assert.That(h.Queue.Items.Single().PostId).IsEqualTo(posted[0].Id);
    }

    [Test]
    public async Task SometimesABotMakesAStoryInstead_UpToItsStoryCap()
    {
        // Stories are stamped with the real time, so the test runs now, in a zone where it is noon.
        var time = new FixedTime(DateTimeOffset.UtcNow);
        var noon = TimeZoneInfo.CreateCustomTimeZone("test-noon", TimeSpan.FromHours(12 - time.Now.UtcDateTime.Hour), "noon", "noon");
        using var h = await FeedHarness.CreateAsync(botCount: 1, feed: new FeedOptions { StoryShare = 1, ChatInspiredShare = 0 }, time: time, clock: new BotClock(noon));
        h.Bots[0].MaxStoriesPerDay = 1;
        await h.Db.SaveChangesAsync();
        h.OpenRouter.GenerateResponseAsync(default!, default, default!, default!, default).ReturnsForAnyArgs(ci =>
            ci.ArgAt<string>(2).Contains("JSON")
                ? """{"background":{"color":"#3f2a1d"},"layers":[{"type":"text","text":"Mise en place","x":50,"y":50,"size":30}]}"""
                : "a plain post today");

        var first = await h.Scheduler.TickAsync(random: () => 0);
        time.Now = time.Now.AddHours(6);
        var second = await h.Scheduler.TickAsync(random: () => 0);

        await Assert.That(first.Stories.Count).IsEqualTo(1);
        await Assert.That(first.Posts).IsEmpty();
        // The day's one story is made, so the next turn is a post.
        await Assert.That(second.Stories).IsEmpty();
        await Assert.That(second.Posts.Count).IsEqualTo(1);
    }

    [Test]
    public async Task WithBotsPostOff_TheSchedulerDoesNothing()
    {
        var time = new FixedTime(new DateTimeOffset(2026, 7, 15, 13, 0, 0, TimeSpan.Zero));
        using var h = await FeedHarness.CreateAsync(feed: new FeedOptions { BotsPost = false }, time: time);
        h.Reply("hello");

        await Assert.That((await h.Scheduler.TickAsync(random: () => 0)).Posts).IsEmpty();
    }
}

/// <summary>What a bot's own post is written from, and that it never quotes a chat.</summary>
public class FeedPostWriterTests
{
    private static async Task<(FeedHarness H, Chat Chat)> WithChatAsync()
    {
        var h = await FeedHarness.CreateAsync(botCount: 1, feed: new FeedOptions { ChatInspiredShare = 1 });
        var chat = new Chat { Name = "cook", CreatedById = h.User.Id, Summary = "Kim burnt the rice and blamed the stove." };
        h.Db.Chats.Add(chat);
        h.Db.ChatMembers.Add(new ChatMember { ChatId = chat.Id, BotId = h.Bots[0].Id });
        h.Db.Messages.Add(new Message { ChatId = chat.Id, SenderUserId = h.User.Id, Content = "my secret recipe uses three spoons of instant coffee in the sauce", SenderUser = null });
        await h.Db.SaveChangesAsync();
        return (h, chat);
    }

    private static FeedPostWriter Writer(FeedHarness h) =>
        new(h.Db, h.OpenRouter, new ConfigurationBuilder().Build(), NullLogger<FeedPostWriter>.Instance,
            Microsoft.Extensions.Options.Options.Create(new FeedOptions { ChatInspiredShare = 1 }), new BotClock(TimeZoneInfo.Utc));

    [Test]
    public async Task ThePrompt_CarriesTheCharacter_TheChatSummary_AndTheTimeOfDay()
    {
        var (h, chat) = await WithChatAsync();
        using var _ = h;
        string? system = null;
        List<ChatMessage>? turns = null;
        h.OpenRouter.GenerateResponseAsync(default!, default, default!, default!, default).ReturnsForAnyArgs(ci =>
        {
            system = ci.ArgAt<string>(2);
            turns = ci.ArgAt<List<ChatMessage>>(3);
            return "what a mess that kitchen was";
        });

        var draft = await Writer(h).WriteAsync(h.Bots[0], new DateTimeOffset(2026, 7, 15, 15, 0, 0, TimeSpan.Zero), roll: 0);

        await Assert.That(draft!.Content).IsEqualTo("what a mess that kitchen was");
        await Assert.That(draft.SourceChatId).IsEqualTo(chat.Id);
        await Assert.That(system!).Contains("You are Gordon, a test character.");
        var all = string.Join("\n", turns!.Select(t => t.Content));
        await Assert.That(all).Contains("Kim burnt the rice and blamed the stove.");
        await Assert.That(all).Contains("never quote");
        await Assert.That(all).Contains("afternoon");
        // People in the chat are never named to the model, so it cannot name them on the feed.
        await Assert.That(all).DoesNotContain("Alice");
        await Assert.That(all).Contains("[Person A]: my secret recipe");
    }

    [Test]
    public async Task APostThatQuotesTheChat_IsWrittenAgain_AndDroppedIfItQuotesTwice()
    {
        var (h, _) = await WithChatAsync();
        using var disposable = h;
        h.Reply("lol my secret recipe uses three spoons of instant coffee", "heard someone puts coffee in sauce. criminal.");

        var draft = await Writer(h).WriteAsync(h.Bots[0], DateTimeOffset.UtcNow, roll: 0);
        await Assert.That(draft!.Content).IsEqualTo("heard someone puts coffee in sauce. criminal.");

        h.Reply("my secret recipe uses three spoons of instant coffee", "three spoons of instant coffee in the sauce, wow");
        await Assert.That(await Writer(h).WriteAsync(h.Bots[0], DateTimeOffset.UtcNow, roll: 0)).IsNull();
    }

    [Test]
    public async Task QuotesChat_CountsOnlyLongRunsOfTheSameWords()
    {
        await Assert.That(FeedPostWriter.QuotesChat("Three spoons of INSTANT coffee in the sauce!", ["three spoons of instant coffee in the sauce"])).IsTrue();
        await Assert.That(FeedPostWriter.QuotesChat("coffee in the sauce, really", ["three spoons of instant coffee in the sauce"])).IsFalse();
    }

    [Test]
    public async Task WithoutRecentChats_ThePostComesFromNowhere()
    {
        using var h = await FeedHarness.CreateAsync(botCount: 1);
        h.Reply("just thinking about knives");
        var draft = await Writer(h).WriteAsync(h.Bots[0], DateTimeOffset.UtcNow, roll: 0);
        await Assert.That(draft!.SourceChatId).IsNull();
    }
}

/// <summary>The bots in a feed thread: one Jev decision per post or comment, as in a chat.</summary>
public class FeedConversationTests
{
    [Test]
    public async Task OnePostAsksJevOnce_AboutEveryOtherBot_InAFeedThread()
    {
        var jev = new FakeJev(name => name == "Kim" ? FakeJev.Chose(BotDecisionEngine.Reply, 0.9) : FakeJev.Chose(BotDecisionEngine.ReactLove, 0.9));
        using var h = await FeedHarness.CreateAsync(botCount: 3, jev: jev);
        h.Reply("the spatula was a gift. noted.");
        var post = await h.PostAsync("someone let a dictator in my kitchen", h.Bots[0]);

        await h.Conversation.ProcessAsync(new FeedEvent(post.Id, null, 1));

        await Assert.That(jev.Calls).Count().IsEqualTo(1);
        var asked = jev.Calls[0].State.Bots.Values.Select(b => b.Name).ToList();
        await Assert.That(asked).IsEquivalentTo(new[] { "Kim", "Beast" });
        await Assert.That(jev.Calls[0].Questions["bot_0"].Instructions).Contains("feed thread");

        var kim = await h.CommentsByAsync(h.Bots[1]);
        await Assert.That(kim.Single().Content).IsEqualTo("the spatula was a gift. noted.");
        await Assert.That(kim.Single().ParentId).IsNull();
        await Assert.That(await h.Db.FeedLikes.CountAsync(l => l.BotId == h.Bots[2].Id && l.PostId == post.Id && l.CommentId == null)).IsEqualTo(1);
        // Kim answered a bot's post: a bot answering a bot ends the chain there.
        await Assert.That(h.Queue.Items).IsEmpty();
        await h.Notifier.ReceivedWithAnyArgs().Typing(default, default, default!, default);
    }

    [Test]
    public async Task WithoutJev_AMentionedBotAnswersAPersonsComment_AsAReplyUnderIt()
    {
        var jev = new FakeJev { Unavailable = true };
        using var h = await FeedHarness.CreateAsync(botCount: 3, jev: jev);
        h.Reply("revanche? any time.");
        var post = await h.PostAsync("cook-off tonight", h.Bots[0]);
        var top = await h.CommentAsync(post, "who wins?", h.Bots[2]);
        var comment = await h.CommentAsync(post, "@Kim you want a rematch?", parentId: top.Id);

        await h.Conversation.ProcessAsync(new FeedEvent(post.Id, comment.Id));

        var kim = await h.CommentsByAsync(h.Bots[1]);
        await Assert.That(kim.Single().ParentId).IsEqualTo(top.Id);
        await Assert.That(await h.CommentsByAsync(h.Bots[0])).IsEmpty();
        await h.Notifier.Received().Reply(h.User.Id, Arg.Is<FeedReplyNoticeDto>(n => n.AuthorName == "Kim" && !n.OnYourPost));
    }

    [Test]
    public async Task ThePersonWhoWasAnswered_AndThePostsAuthor_AreNotified()
    {
        var jev = new FakeJev(_ => FakeJev.Chose(BotDecisionEngine.Reply, 0.95));
        using var h = await FeedHarness.CreateAsync(botCount: 1, jev: jev);
        h.Reply("nice post");
        var post = await h.PostAsync("my first post");

        await h.Conversation.ProcessAsync(new FeedEvent(post.Id, null));

        await h.Notifier.Received(1).Reply(h.User.Id, Arg.Is<FeedReplyNoticeDto>(n => n.OnYourPost && n.Content == "nice post"));
    }

    [Test]
    public async Task TheHopLimit_EndsBotsAnsweringBots()
    {
        var jev = new FakeJev(_ => FakeJev.Chose(BotDecisionEngine.Reply, 0.95));
        using var h = await FeedHarness.CreateAsync(botCount: 2, jev: jev);
        h.Reply("another take on it, a new one");
        var post = await h.PostAsync("hot take", h.Bots[0]);
        var deep = await h.CommentAsync(post, "deep in the chain", h.Bots[1]);

        var limit = new FeedOptions().MaxBotChainDepth;
        await h.Conversation.ProcessAsync(new FeedEvent(post.Id, deep.Id, limit));

        await Assert.That(await h.CommentsByAsync(h.Bots[0])).Count().IsEqualTo(1);
        await Assert.That(h.Queue.Items).IsEmpty();
    }

    [Test]
    public async Task BotsWithTheFeedOff_OrPrivate_StayOutOfThreads()
    {
        var jev = new FakeJev(_ => FakeJev.Chose(BotDecisionEngine.Reply, 0.95));
        using var h = await FeedHarness.CreateAsync(botCount: 3, jev: jev);
        h.Bots[1].FeedEnabled = false;
        h.Bots[2].IsPublic = false;
        await h.Db.SaveChangesAsync();
        h.Reply("hi");
        var post = await h.PostAsync("anyone around?");

        await h.Conversation.ProcessAsync(new FeedEvent(post.Id, null));

        await Assert.That(jev.Calls.Single().State.Bots.Values.Select(b => b.Name)).IsEquivalentTo(new[] { "Gordon" });
    }
}

/// <summary>Who sees what, and what people can do with posts.</summary>
public class FeedVisibilityTests
{
    [Test]
    public async Task EveryoneSeesPeoplesAndPublicBotsPosts_NotPrivateOrDeletedBots()
    {
        using var h = await FeedHarness.CreateAsync(botCount: 3);
        h.Bots[1].IsPublic = false;
        h.Bots[2].IsActive = false;
        await h.Db.SaveChangesAsync();
        await h.PostAsync("by a person");
        await h.PostAsync("by a public bot", h.Bots[0]);
        await h.PostAsync("by a private bot", h.Bots[1]);
        await h.PostAsync("by a deleted bot", h.Bots[2]);
        var reader = new User { ExternalId = "ext-2", Email = "reader@example.com", DisplayName = "Bob" };
        h.Db.Users.Add(reader);
        await h.Db.SaveChangesAsync();

        var page = await new GetFeedHandler(h.Db, AppAdmins.None).Handle(new GetFeedQuery(reader.Id), default);

        await Assert.That(page.Value!.Posts.Select(p => p.Content)).IsEquivalentTo(new[] { "by a person", "by a public bot" });
        await Assert.That(page.Value.CanModerate).IsFalse();
    }

    [Test]
    public async Task OnlyTheAuthorOrAnAdmin_DeletesAPost()
    {
        using var h = await FeedHarness.CreateAsync(botCount: 1);
        var post = await h.PostAsync("mine");
        var botPost = await h.PostAsync("a bot's", h.Bots[0]);
        var other = new User { ExternalId = "ext-2", Email = "admin@example.com", DisplayName = "Admin" };
        h.Db.Users.Add(other);
        await h.Db.SaveChangesAsync();

        var refused = await new DeletePostHandler(h.Db, AppAdmins.None, h.Notifier).Handle(new DeletePostCommand(other.Id, post.Id), default);
        await Assert.That(refused.IsSuccess).IsFalse();

        var own = await new DeletePostHandler(h.Db, AppAdmins.None, h.Notifier).Handle(new DeletePostCommand(h.User.Id, post.Id), default);
        var admin = await new DeletePostHandler(h.Db, new AppAdmins(["admin@example.com"]), h.Notifier).Handle(new DeletePostCommand(other.Id, botPost.Id), default);
        await Assert.That(own.IsSuccess && admin.IsSuccess).IsTrue();
        await Assert.That(await h.Db.Posts.CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task AReplyToAReply_HangsUnderItsTopLevelComment_AndWakesTheBots()
    {
        using var h = await FeedHarness.CreateAsync(botCount: 1);
        var post = await h.PostAsync("thread", h.Bots[0]);
        var top = await h.CommentAsync(post, "top", h.Bots[0]);
        var reply = await h.CommentAsync(post, "reply", h.Bots[0], parentId: top.Id);

        var result = await new CreateCommentHandler(h.Db, h.Notifier, h.Queue).Handle(new CreateCommentCommand(h.User.Id, post.Id, "and me", reply.Id), default);

        await Assert.That(result.Value!.ParentId).IsEqualTo(top.Id);
        await Assert.That(h.Queue.Items.Single()).IsEqualTo(new FeedEvent(post.Id, result.Value.Id));
    }

    [Test]
    public async Task Likes_ToggleOnAndOff_PerPerson()
    {
        using var h = await FeedHarness.CreateAsync(botCount: 1);
        var post = await h.PostAsync("like me", h.Bots[0]);
        var handler = new ToggleFeedLikeHandler(h.Db, h.Notifier);

        var on = await handler.Handle(new ToggleFeedLikeCommand(h.User.Id, post.Id), default);
        var off = await handler.Handle(new ToggleFeedLikeCommand(h.User.Id, post.Id), default);

        await Assert.That(on.Value!.LikeCount).IsEqualTo(1);
        await Assert.That(on.Value.Liked).IsTrue();
        await Assert.That(off.Value!.LikeCount).IsEqualTo(0);
    }

    [Test]
    public async Task Unread_CountsOthersPostsAndRepliesToYou_SinceYouLastLooked()
    {
        using var h = await FeedHarness.CreateAsync(botCount: 1);
        h.User.FeedSeenAt = DateTime.UtcNow.AddMinutes(-10);
        await h.Db.SaveChangesAsync();
        await h.PostAsync("old", h.Bots[0], at: DateTime.UtcNow.AddHours(-1));
        await h.PostAsync("new", h.Bots[0]);
        var mine = await h.PostAsync("mine", at: DateTime.UtcNow.AddHours(-1));
        await h.CommentAsync(mine, "a reply to you", h.Bots[0]);

        var unread = await new GetFeedUnreadHandler(h.Db).Handle(new GetFeedUnreadQuery(h.User.Id), default);
        await Assert.That(unread.Value).IsEqualTo(2);
    }
}
