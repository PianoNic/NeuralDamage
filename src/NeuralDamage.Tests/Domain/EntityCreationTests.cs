using NeuralDamage.Domain;
using NeuralDamage.Domain.Enums;

namespace NeuralDamage.Tests.Domain;

public class EntityCreationTests
{
    [Fact]
    public void Bot_DefaultValues_AreCorrect()
    {
        var bot = new Bot { Name = "TestBot", ModelId = "openai/gpt-4o", CreatedById = Guid.NewGuid() };

        Assert.True(bot.IsActive);
        Assert.Equal(0.7, bot.Temperature);
        Assert.Null(bot.Aliases);
        Assert.Null(bot.Personality);
        Assert.Null(bot.AvatarUrl);
        Assert.Equal(string.Empty, bot.SystemPrompt);
        Assert.NotEqual(Guid.Empty, bot.Id);
    }

    [Fact]
    public void Chat_HasRequiredFields()
    {
        var userId = Guid.NewGuid();
        var chat = new Chat { Name = "Test Chat", CreatedById = userId };

        Assert.Equal("Test Chat", chat.Name);
        Assert.Equal(userId, chat.CreatedById);
        Assert.NotEqual(Guid.Empty, chat.Id);
    }

    [Fact]
    public void ChatMember_DefaultValues_AreCorrect()
    {
        var member = new ChatMember { ChatId = Guid.NewGuid(), UserId = Guid.NewGuid() };

        Assert.Equal(ChatMemberRole.Member, member.Role);
        Assert.True(member.JoinedAt <= DateTime.UtcNow);
        Assert.True(member.JoinedAt > DateTime.UtcNow.AddSeconds(-5));
    }

    [Fact]
    public void ChatMember_CanBeUserOrBot()
    {
        var userMember = new ChatMember { ChatId = Guid.NewGuid(), UserId = Guid.NewGuid() };
        var botMember = new ChatMember { ChatId = Guid.NewGuid(), BotId = Guid.NewGuid() };

        Assert.NotNull(userMember.UserId);
        Assert.Null(userMember.BotId);
        Assert.Null(botMember.UserId);
        Assert.NotNull(botMember.BotId);
    }

    [Fact]
    public void Message_HasRequiredFields()
    {
        var msg = new Message { ChatId = Guid.NewGuid(), SenderUserId = Guid.NewGuid(), Content = "Hello" };

        Assert.Equal("Hello", msg.Content);
        Assert.NotNull(msg.SenderUserId);
        Assert.Null(msg.SenderBotId);
        Assert.Null(msg.ReplyToId);
        Assert.Null(msg.Mentions);
    }

    [Fact]
    public void Reaction_HasRequiredFields()
    {
        var reaction = new Reaction { MessageId = Guid.NewGuid(), UserId = Guid.NewGuid(), Emoji = "👍" };

        Assert.Equal("👍", reaction.Emoji);
        Assert.NotNull(reaction.UserId);
        Assert.Null(reaction.BotId);
    }

    [Fact]
    public void BaseEntity_SetsCreatedAtAndId()
    {
        var bot = new Bot { Name = "Test", ModelId = "test/model", CreatedById = Guid.NewGuid() };

        Assert.NotEqual(Guid.Empty, bot.Id);
        Assert.True(bot.CreatedAt <= DateTime.UtcNow);
        Assert.True(bot.CreatedAt > DateTime.UtcNow.AddSeconds(-5));
    }
}
