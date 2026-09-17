using NeuralDamage.Domain;
using NeuralDamage.Domain.Enums;

namespace NeuralDamage.Tests.Domain;

public class EntityCreationTests
{
    [Test]
    public async Task Bot_DefaultValues_AreCorrect()
    {
        var bot = new Bot { Name = "TestBot", ModelId = "openai/gpt-4o", CreatedById = Guid.NewGuid() };

        await Assert.That(bot.IsActive).IsTrue();
        await Assert.That(bot.Temperature).IsEqualTo(0.7);
        await Assert.That(bot.Aliases).IsNull();
        await Assert.That(bot.Personality).IsNull();
        await Assert.That(bot.AvatarUrl).IsNull();
        await Assert.That(bot.SystemPrompt).IsEqualTo(string.Empty);
        await Assert.That(bot.Id).IsNotEqualTo(Guid.Empty);
    }

    [Test]
    public async Task Chat_HasRequiredFields()
    {
        var userId = Guid.NewGuid();
        var chat = new Chat { Name = "Test Chat", CreatedById = userId };

        await Assert.That(chat.Name).IsEqualTo("Test Chat");
        await Assert.That(chat.CreatedById).IsEqualTo(userId);
        await Assert.That(chat.Id).IsNotEqualTo(Guid.Empty);
    }

    [Test]
    public async Task ChatMember_DefaultValues_AreCorrect()
    {
        var member = new ChatMember { ChatId = Guid.NewGuid(), UserId = Guid.NewGuid() };

        await Assert.That(member.Role).IsEqualTo(ChatMemberRole.Member);
        await Assert.That(member.JoinedAt <= DateTime.UtcNow).IsTrue();
        await Assert.That(member.JoinedAt > DateTime.UtcNow.AddSeconds(-5)).IsTrue();
    }

    [Test]
    public async Task ChatMember_CanBeUserOrBot()
    {
        var userMember = new ChatMember { ChatId = Guid.NewGuid(), UserId = Guid.NewGuid() };
        var botMember = new ChatMember { ChatId = Guid.NewGuid(), BotId = Guid.NewGuid() };

        await Assert.That(userMember.UserId).IsNotNull();
        await Assert.That(userMember.BotId).IsNull();
        await Assert.That(botMember.UserId).IsNull();
        await Assert.That(botMember.BotId).IsNotNull();
    }

    [Test]
    public async Task Message_HasRequiredFields()
    {
        var msg = new Message { ChatId = Guid.NewGuid(), SenderUserId = Guid.NewGuid(), Content = "Hello" };

        await Assert.That(msg.Content).IsEqualTo("Hello");
        await Assert.That(msg.SenderUserId).IsNotNull();
        await Assert.That(msg.SenderBotId).IsNull();
        await Assert.That(msg.ReplyToId).IsNull();
        await Assert.That(msg.Mentions).IsNull();
    }

    [Test]
    public async Task Reaction_HasRequiredFields()
    {
        var reaction = new Reaction { MessageId = Guid.NewGuid(), UserId = Guid.NewGuid(), Emoji = "👍" };

        await Assert.That(reaction.Emoji).IsEqualTo("👍");
        await Assert.That(reaction.UserId).IsNotNull();
        await Assert.That(reaction.BotId).IsNull();
    }

    [Test]
    public async Task BaseEntity_SetsCreatedAtAndId()
    {
        var bot = new Bot { Name = "Test", ModelId = "test/model", CreatedById = Guid.NewGuid() };

        await Assert.That(bot.Id).IsNotEqualTo(Guid.Empty);
        await Assert.That(bot.CreatedAt <= DateTime.UtcNow).IsTrue();
        await Assert.That(bot.CreatedAt > DateTime.UtcNow.AddSeconds(-5)).IsTrue();
    }
}
