using NeuralDamage.Infrastructure.Services.BotDecision;
using NeuralDamage.Domain;

namespace NeuralDamage.Tests.BotDecision;

public class Tier1HardRulesTests
{
    private static Bot MakeBot(string name = "TestBot", string? aliases = null, bool active = true) =>
        new() { Name = name, ModelId = "test", SystemPrompt = "x", CreatedById = Guid.NewGuid(), Aliases = aliases, IsActive = active };

    private static Message MakeMessage(string content, Guid? senderBotId = null, Message? replyTo = null) =>
        new() { ChatId = Guid.NewGuid(), Content = content, SenderUserId = senderBotId is null ? Guid.NewGuid() : null, SenderBotId = senderBotId, ReplyTo = replyTo, ReplyToId = replyTo?.Id };

    [Test]
    public async Task InactiveBot_MustSkip()
    {
        var bot = MakeBot(active: false);
        var msg = MakeMessage("hello");
        await Assert.That(Tier1HardRules.Evaluate(msg, bot, false, false)).IsEqualTo(Tier1Result.MustSkip);
    }

    [Test]
    public async Task MutedChat_MustSkip()
    {
        var bot = MakeBot();
        var msg = MakeMessage("hello");
        await Assert.That(Tier1HardRules.Evaluate(msg, bot, isMuted: true, isStopped: false)).IsEqualTo(Tier1Result.MustSkip);
    }

    [Test]
    public async Task StoppedChat_MustSkip()
    {
        var bot = MakeBot();
        var msg = MakeMessage("hello");
        await Assert.That(Tier1HardRules.Evaluate(msg, bot, isMuted: false, isStopped: true)).IsEqualTo(Tier1Result.MustSkip);
    }

    [Test]
    public async Task SlashCommand_MustSkip()
    {
        var bot = MakeBot();
        var msg = MakeMessage("/help");
        await Assert.That(Tier1HardRules.Evaluate(msg, bot, false, false)).IsEqualTo(Tier1Result.MustSkip);
    }

    [Test]
    public async Task BotToBot_NoMention_MustSkip()
    {
        var bot = MakeBot("GPT");
        var msg = MakeMessage("I think so too", senderBotId: Guid.NewGuid());
        await Assert.That(Tier1HardRules.Evaluate(msg, bot, false, false)).IsEqualTo(Tier1Result.MustSkip);
    }

    [Test]
    public async Task BotToBot_WithMention_MustRespond()
    {
        var bot = MakeBot("GPT");
        var msg = MakeMessage("hey GPT what do you think?", senderBotId: Guid.NewGuid());
        await Assert.That(Tier1HardRules.Evaluate(msg, bot, false, false)).IsEqualTo(Tier1Result.MustRespond);
    }

    [Test]
    public async Task GroupAddress_MustRespond()
    {
        var bot = MakeBot();
        var msg = MakeMessage("hey everyone what's up?");
        await Assert.That(Tier1HardRules.Evaluate(msg, bot, false, false)).IsEqualTo(Tier1Result.MustRespond);
    }

    [Test]
    public async Task NameMentioned_MustRespond()
    {
        var bot = MakeBot("Sarah");
        var msg = MakeMessage("sarah do you agree?");
        await Assert.That(Tier1HardRules.Evaluate(msg, bot, false, false)).IsEqualTo(Tier1Result.MustRespond);
    }

    [Test]
    public async Task AliasMentioned_MustRespond()
    {
        var bot = MakeBot("Professor Einstein", aliases: "prof,al");
        var msg = MakeMessage("hey prof what's the answer?");
        await Assert.That(Tier1HardRules.Evaluate(msg, bot, false, false)).IsEqualTo(Tier1Result.MustRespond);
    }

    [Test]
    public async Task ReplyToBot_MustRespond()
    {
        var bot = MakeBot();
        var botMessage = new Message { ChatId = Guid.NewGuid(), SenderBotId = bot.Id, Content = "earlier" };
        var msg = new Message { ChatId = Guid.NewGuid(), SenderUserId = Guid.NewGuid(), Content = "I disagree", ReplyToId = botMessage.Id, ReplyTo = botMessage };
        await Assert.That(Tier1HardRules.Evaluate(msg, bot, false, false)).IsEqualTo(Tier1Result.MustRespond);
    }

    [Test]
    public async Task NormalMessage_Undecided()
    {
        var bot = MakeBot("GPT");
        var msg = MakeMessage("the weather is nice today");
        await Assert.That(Tier1HardRules.Evaluate(msg, bot, false, false)).IsEqualTo(Tier1Result.Undecided);
    }
}
