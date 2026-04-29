using NeuralDamage.Infrastructure.Services.BotDecision;
using NeuralDamage.Domain;

namespace NeuralDamage.Tests.BotDecision;

public class Tier1HardRulesTests
{
    private static Bot MakeBot(string name = "TestBot", string? aliases = null, bool active = true) =>
        new() { Name = name, ModelId = "test", SystemPrompt = "x", CreatedById = Guid.NewGuid(), Aliases = aliases, IsActive = active };

    private static Message MakeMessage(string content, Guid? senderBotId = null, Message? replyTo = null) =>
        new() { ChatId = Guid.NewGuid(), Content = content, SenderUserId = senderBotId is null ? Guid.NewGuid() : null, SenderBotId = senderBotId, ReplyTo = replyTo, ReplyToId = replyTo?.Id };

    [Fact]
    public void InactiveBot_MustSkip()
    {
        var bot = MakeBot(active: false);
        var msg = MakeMessage("hello");
        Assert.Equal(Tier1Result.MustSkip, Tier1HardRules.Evaluate(msg, bot, false, false));
    }

    [Fact]
    public void MutedChat_MustSkip()
    {
        var bot = MakeBot();
        var msg = MakeMessage("hello");
        Assert.Equal(Tier1Result.MustSkip, Tier1HardRules.Evaluate(msg, bot, isMuted: true, isStopped: false));
    }

    [Fact]
    public void StoppedChat_MustSkip()
    {
        var bot = MakeBot();
        var msg = MakeMessage("hello");
        Assert.Equal(Tier1Result.MustSkip, Tier1HardRules.Evaluate(msg, bot, isMuted: false, isStopped: true));
    }

    [Fact]
    public void SlashCommand_MustSkip()
    {
        var bot = MakeBot();
        var msg = MakeMessage("/help");
        Assert.Equal(Tier1Result.MustSkip, Tier1HardRules.Evaluate(msg, bot, false, false));
    }

    [Fact]
    public void BotToBot_NoMention_MustSkip()
    {
        var bot = MakeBot("GPT");
        var msg = MakeMessage("I think so too", senderBotId: Guid.NewGuid());
        Assert.Equal(Tier1Result.MustSkip, Tier1HardRules.Evaluate(msg, bot, false, false));
    }

    [Fact]
    public void BotToBot_WithMention_MustRespond()
    {
        var bot = MakeBot("GPT");
        var msg = MakeMessage("hey GPT what do you think?", senderBotId: Guid.NewGuid());
        Assert.Equal(Tier1Result.MustRespond, Tier1HardRules.Evaluate(msg, bot, false, false));
    }

    [Fact]
    public void GroupAddress_MustRespond()
    {
        var bot = MakeBot();
        var msg = MakeMessage("hey everyone what's up?");
        Assert.Equal(Tier1Result.MustRespond, Tier1HardRules.Evaluate(msg, bot, false, false));
    }

    [Fact]
    public void NameMentioned_MustRespond()
    {
        var bot = MakeBot("Sarah");
        var msg = MakeMessage("sarah do you agree?");
        Assert.Equal(Tier1Result.MustRespond, Tier1HardRules.Evaluate(msg, bot, false, false));
    }

    [Fact]
    public void AliasMentioned_MustRespond()
    {
        var bot = MakeBot("Professor Einstein", aliases: "prof,al");
        var msg = MakeMessage("hey prof what's the answer?");
        Assert.Equal(Tier1Result.MustRespond, Tier1HardRules.Evaluate(msg, bot, false, false));
    }

    [Fact]
    public void ReplyToBot_MustRespond()
    {
        var bot = MakeBot();
        var botMessage = new Message { ChatId = Guid.NewGuid(), SenderBotId = bot.Id, Content = "earlier" };
        var msg = new Message { ChatId = Guid.NewGuid(), SenderUserId = Guid.NewGuid(), Content = "I disagree", ReplyToId = botMessage.Id, ReplyTo = botMessage };
        Assert.Equal(Tier1Result.MustRespond, Tier1HardRules.Evaluate(msg, bot, false, false));
    }

    [Fact]
    public void NormalMessage_Undecided()
    {
        var bot = MakeBot("GPT");
        var msg = MakeMessage("the weather is nice today");
        Assert.Equal(Tier1Result.Undecided, Tier1HardRules.Evaluate(msg, bot, false, false));
    }
}
