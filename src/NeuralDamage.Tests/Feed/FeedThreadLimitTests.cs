using Microsoft.EntityFrameworkCore;
using NeuralDamage.Domain;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.BotDecision;
using NeuralDamage.Infrastructure.Services.Feed;
using NeuralDamage.Tests.Helpers;

namespace NeuralDamage.Tests.Feed;

/// <summary>What keeps a feed thread short: the caps, the bar for commenting, and no echoes.</summary>
public class FeedThreadLimitTests
{
    private static readonly DateTime Earlier = DateTime.UtcNow.AddMinutes(-10);

    private static FakeJev EveryoneComments(double p = 0.95) => new(_ => FakeJev.Chose(BotDecisionEngine.Reply, p));

    private static Task<int> LikesByAsync(FeedHarness h, Bot bot) => h.Db.FeedLikes.CountAsync(l => l.BotId == bot.Id);

    [Test]
    public async Task OnceThePostHasEnoughBotComments_BotsLikeInstead_UnlessAPersonAddressesOne()
    {
        using var h = await FeedHarness.CreateAsync(botCount: 4, jev: EveryoneComments(), feed: new FeedOptions { MaxBotCommentsPerPost = 2 });
        h.Reply("count me in for the next market run", "orange tomatoes are a scam and you know it");
        var post = await h.PostAsync("market haul today", at: Earlier);
        await h.CommentAsync(post, "the peaches looked fake to me", h.Bots[0], at: Earlier);
        await h.CommentAsync(post, "the bread was a crime", h.Bots[3], at: Earlier);

        var nobody = await h.CommentAsync(post, "anyone else go?");
        await h.Conversation.ProcessAsync(new FeedEvent(post.Id, nobody.Id));

        await Assert.That(await h.CommentsByAsync(h.Bots[1])).IsEmpty();
        await Assert.That(await h.CommentsByAsync(h.Bots[2])).IsEmpty();
        await Assert.That(await LikesByAsync(h, h.Bots[1])).IsEqualTo(1);

        var toKim = await h.CommentAsync(post, "@Kim you went too, right?");
        await h.Conversation.ProcessAsync(new FeedEvent(post.Id, toKim.Id));

        await Assert.That(await h.CommentsByAsync(h.Bots[1])).Count().IsEqualTo(1);
        await Assert.That(await h.CommentsByAsync(h.Bots[2])).IsEmpty();
    }

    [Test]
    public async Task ABotCommentsTwicePerThread_ThenOnlyWhenAddressed()
    {
        using var h = await FeedHarness.CreateAsync(botCount: 2, jev: EveryoneComments());
        h.Reply("third time is the charm, right here", "fine, one more about the rice then");
        var post = await h.PostAsync("rice or noodles?", at: Earlier);
        var first = await h.CommentAsync(post, "rice, obviously, every single day", h.Bots[1], at: Earlier);
        await h.CommentAsync(post, "noodles are for quitters", h.Bots[1], parentId: first.Id, at: Earlier);

        var open = await h.CommentAsync(post, "noodles for me");
        await h.Conversation.ProcessAsync(new FeedEvent(post.Id, open.Id));

        await Assert.That(await h.CommentsByAsync(h.Bots[1])).Count().IsEqualTo(2);
        await Assert.That(await LikesByAsync(h, h.Bots[1])).IsEqualTo(1);
        await Assert.That(await h.CommentsByAsync(h.Bots[0])).Count().IsEqualTo(1);

        // A person answering Kim's comment addresses Kim, past the cap.
        var answer = await h.CommentAsync(post, "why do you hate noodles so much", parentId: first.Id);
        await h.Conversation.ProcessAsync(new FeedEvent(post.Id, answer.Id));

        await Assert.That(await h.CommentsByAsync(h.Bots[1])).Count().IsEqualTo(3);
    }

    [Test]
    public async Task AtMostTwoBotsAnswerOneComment_TheOthersLike()
    {
        using var h = await FeedHarness.CreateAsync(botCount: 4, jev: EveryoneComments());
        h.Reply("the crust looks burnt from here", "that cheese pull is unreal", "who puts pineapple on that", "nice oven, where is it from");
        var post = await h.PostAsync("homemade pizza night");

        await h.Conversation.ProcessAsync(new FeedEvent(post.Id, null));

        var comments = await h.Db.PostComments.CountAsync(c => c.AuthorBotId != null);
        var likes = await h.Db.FeedLikes.CountAsync(l => l.BotId != null);
        await Assert.That(comments).IsEqualTo(2);
        await Assert.That(likes).IsEqualTo(2);
    }

    [Test]
    public async Task BotsAnswerABot_OneLevelDeep_AndThatEndsTheChain()
    {
        using var h = await FeedHarness.CreateAsync(botCount: 2, jev: EveryoneComments());
        h.Reply("salt first, always, no debate", "pepper first and you know it", "both at once like a real cook");
        var post = await h.PostAsync("salt or pepper first?");
        var mine = await h.CommentAsync(post, "salt, I think");

        await h.Conversation.ProcessAsync(new FeedEvent(post.Id, mine.Id));
        var botComment = h.Queue.Items.First();
        await Assert.That(botComment.Depth).IsEqualTo(1);

        h.Queue.Items.Clear();
        await h.Conversation.ProcessAsync(botComment);

        await Assert.That(await h.Db.PostComments.CountAsync(c => c.AuthorBotId != null)).IsGreaterThan(2);
        await Assert.That(h.Queue.Items).IsEmpty();
    }

    [Test]
    public async Task AWeakComment_BecomesALike_UnlessThePersonAddressedTheBot()
    {
        // 0.65 clears the chat's reply threshold (0.6), not the feed's comment one.
        using var h = await FeedHarness.CreateAsync(botCount: 1, jev: EveryoneComments(0.65));
        h.Reply("you rang? I was right here");
        var post = await h.PostAsync("long day");

        await h.Conversation.ProcessAsync(new FeedEvent(post.Id, null));

        await Assert.That(await h.CommentsByAsync(h.Bots[0])).IsEmpty();
        await Assert.That(await LikesByAsync(h, h.Bots[0])).IsEqualTo(1);

        var mention = await h.CommentAsync(post, "@Gordon what do you think?");
        await h.Conversation.ProcessAsync(new FeedEvent(post.Id, mention.Id));

        await Assert.That(await h.CommentsByAsync(h.Bots[0])).Count().IsEqualTo(1);
    }

    [Test]
    public async Task ACommentThatSaysWhatTheThreadAlreadySays_IsDropped()
    {
        using var h = await FeedHarness.CreateAsync(botCount: 3, jev: EveryoneComments());
        var post = await h.PostAsync("found an orange tomato at the market", at: Earlier);
        await h.CommentAsync(post, "an orange tomato is a crime against salads", h.Bots[2], at: Earlier);
        h.Reply("An orange tomato is a CRIME against salads!");

        var mine = await h.CommentAsync(post, "i saw it");
        await h.Conversation.ProcessAsync(new FeedEvent(post.Id, mine.Id));

        await Assert.That(await h.CommentsByAsync(h.Bots[0])).IsEmpty();
        await Assert.That(await h.CommentsByAsync(h.Bots[1])).IsEmpty();
    }

    [Test]
    public async Task TwoBotsSayingTheSameInOneRound_OnlyTheFirstIsPosted()
    {
        using var h = await FeedHarness.CreateAsync(botCount: 2, jev: EveryoneComments());
        h.Reply("you saw it, good, now never speak of it");
        var post = await h.PostAsync("did anyone see the sunset?");

        await h.Conversation.ProcessAsync(new FeedEvent(post.Id, null));

        await Assert.That(await h.Db.PostComments.CountAsync()).IsEqualTo(1);
    }

    [Test]
    public async Task TheFeedCommentPrompt_CarriesTheCommentRules()
    {
        var bot = new Bot { Name = "Rex", ModelId = "m", CreatedById = Guid.Empty, SystemPrompt = "You are Rex." };

        var system = FeedConversation.SystemPrompt(bot, "Alice", null);
        await Assert.That(system).Contains(FeedConversation.CommentRules);
        await Assert.That(system).Contains("one short line");
        await Assert.That(system).Contains("catchphrases");
        await Assert.That(system).Contains("a concrete detail");
        await Assert.That(system).Contains("Don't echo or paraphrase");
        await Assert.That(system).Contains("ALL CAPS");

        var note = FeedConversation.CommentNote(bot, PeopleLanguage.Unknown);
        await Assert.That(note.Content).Contains("one short line");
        await Assert.That(note.Content).Contains("catchphrases");
    }

    [Test]
    public async Task ChatPrompts_StayWithoutTheFeedRules()
    {
        var bot = new Bot { Name = "Rex", ModelId = "m", CreatedById = Guid.Empty, SystemPrompt = "You are Rex." };

        var chat = BotPromptBuilder.BuildSystemPrompt(bot, ["Alice"]);
        await Assert.That(chat).DoesNotContain("Commenting on the feed");
        await Assert.That(chat).DoesNotContain("catchphrases");
        await Assert.That(BotPromptBuilder.BuildNote(bot: bot).Content).DoesNotContain("catchphrases");
    }
}
