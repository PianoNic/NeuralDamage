using Microsoft.Extensions.Logging.Abstractions;
using NeuralDamage.Domain;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.BotDecision;
using NeuralDamage.Tests.Helpers;
using static NeuralDamage.Infrastructure.Services.BotDecision.BotDecisionEngine;

namespace NeuralDamage.Tests.Stories;

/// <summary>A reply to a story reads, for the bots, as a reply to that story, not as a picture the person sent.</summary>
public class StoryReplyPromptTests
{
    private static readonly User Alice = new() { ExternalId = "a", Email = "a@test.com", DisplayName = "Alice" };

    private static Message StoryReply(string content, Guid? authorBotId, string authorName, string storyText)
    {
        var message = new Message
        {
            ChatId = Guid.NewGuid(),
            SenderUserId = Alice.Id,
            SenderUser = Alice,
            Content = content,
            StoryId = Guid.NewGuid(),
            StoryAuthorBotId = authorBotId,
            StoryAuthorName = authorName,
            StoryText = storyText,
        };
        // The story's picture, attached for people.
        message.Attachments.Add(new Attachment { ChatId = message.ChatId, UploaderUserId = Alice.Id, ContentType = "image/png", SizeBytes = 64, Description = $"{authorName}'s story: {storyText}" });
        return message;
    }

    [Test]
    public async Task AReplyToTheBotsOwnStory_ReadsAsAReplyToYourStory_WithoutThePicture()
    {
        var mom = Guid.NewGuid();
        var message = StoryReply("💀", mom, "Mom", "Up since 5. Making breakfast. For NO ONE");
        var images = new Dictionary<Guid, ImagePart> { [message.Attachments.Single().Id] = new("image/png", TestImages.Png()) };

        var history = BotPromptBuilder.BuildHistory([message], mom, images: images);

        await Assert.That(history.Single().Content)
            .IsEqualTo("[Alice replied to your story (\"Up since 5. Making breakfast. For NO ONE\")] (you're answering this): 💀");
        await Assert.That(history.Single().Images).IsEmpty();
    }

    [Test]
    public async Task AReplyToSomeoneElsesStory_NamesWhoseStoryItWas()
    {
        var message = StoryReply("so jealous", Guid.NewGuid(), "Kim", "Beach day");

        var history = BotPromptBuilder.BuildHistory([message], Guid.NewGuid());

        await Assert.That(history.Single().Content)
            .IsEqualTo("[Alice replied to Kim's story (\"Beach day\")] (you're answering this): so jealous");
        // Jev and the summary, which have no header, get it in the text.
        await Assert.That(BotPromptBuilder.WithImages(message)).IsEqualTo("[replied to Kim's story (\"Beach day\")] so jealous");
    }

    [Test]
    public async Task ThePrompt_TellsTheBotHowToTakeAReplyToItsStory()
    {
        var bot = new Bot { Name = "Mom", ModelId = "m", CreatedById = Guid.NewGuid() };

        var prompt = BotPromptBuilder.BuildSystemPrompt(bot, ["Alice", "Mom"]);

        await Assert.That(prompt).Contains("replied to your story");
        await Assert.That(prompt).Contains("don't describe your story back to them");
    }

    [Test]
    public async Task Jev_AStoryReplyIsAddressedToTheStorysAuthor()
    {
        var jev = new FakeJev(_ => FakeJev.Chose(Reply, 0.95));
        using var h = await OrchestratorHarness.CreateAsync(botCount: 2);
        var message = await StoryReplyInChatAsync(h, h.Bots[1]);

        var verdicts = await new BotDecisionEngine(h.Db, jev, new BotRankingOptions(), NullLogger<BotDecisionEngine>.Instance)
            .DecideAsync(h.Chat.Id, message, h.Bots);

        await Assert.That(verdicts.Where(v => v.Action == BotAction.Reply).Select(v => v.Bot.Name)).IsEquivalentTo([h.Bots[1].Name]);
        await Assert.That(jev.Calls.Single().State.NewMessage.ReplyTo).IsEqualTo(h.Bots[1].Name);
        await Assert.That(jev.Calls.Single().State.NewMessage.Text).StartsWith($"[replied to {h.Bots[1].Name}'s story");
    }

    [Test]
    public async Task JevUnavailable_TheStorysAuthorAnswers()
    {
        var jev = new FakeJev { Unavailable = true };
        using var h = await OrchestratorHarness.CreateAsync(botCount: 2);
        var message = await StoryReplyInChatAsync(h, h.Bots[1]);

        var verdicts = await new BotDecisionEngine(h.Db, jev, new BotRankingOptions(), NullLogger<BotDecisionEngine>.Instance)
            .DecideAsync(h.Chat.Id, message, h.Bots);

        await Assert.That(verdicts.Where(v => v.Action == BotAction.Reply).Select(v => v.Bot.Name)).IsEquivalentTo([h.Bots[1].Name]);
    }

    private static async Task<Message> StoryReplyInChatAsync(OrchestratorHarness h, Bot author)
    {
        var message = new Message
        {
            ChatId = h.Chat.Id,
            SenderUserId = h.User.Id,
            Content = "🔥",
            StoryId = Guid.NewGuid(),
            StoryAuthorBotId = author.Id,
            StoryAuthorName = author.Name,
            StoryText = "New haircut",
        };
        h.Db.Messages.Add(message);
        await h.Db.SaveChangesAsync();
        return message;
    }
}
