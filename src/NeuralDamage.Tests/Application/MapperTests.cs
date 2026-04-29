using NeuralDamage.Infrastructure.Mappers;
using NeuralDamage.Domain;
using NeuralDamage.Domain.Enums;

namespace NeuralDamage.Tests.Application;

public class MapperTests
{
    [Fact]
    public void Bot_ToDto_MapsAllFields()
    {
        var bot = new Bot { Name = "TestBot", ModelId = "openai/gpt-4o", SystemPrompt = "Be helpful", Personality = "Friendly", Temperature = 0.9, AvatarUrl = "https://example.com/avatar.png", Aliases = "tb,test", CreatedById = Guid.NewGuid(), IsActive = true };

        var dto = bot.ToDto();

        Assert.Equal(bot.Id, dto.Id);
        Assert.Equal("TestBot", dto.Name);
        Assert.Equal("openai/gpt-4o", dto.ModelId);
        Assert.Equal("Be helpful", dto.SystemPrompt);
        Assert.Equal("Friendly", dto.Personality);
        Assert.Equal(0.9, dto.Temperature);
        Assert.Equal("https://example.com/avatar.png", dto.AvatarUrl);
        Assert.Equal("tb,test", dto.Aliases);
        Assert.True(dto.IsActive);
    }

    [Fact]
    public void Bot_ToSummaryDto_MapsCorrectFields()
    {
        var bot = new Bot { Name = "TestBot", ModelId = "test", CreatedById = Guid.NewGuid() };

        var dto = bot.ToSummaryDto();

        Assert.Equal(bot.Id, dto.Id);
        Assert.Equal("TestBot", dto.Name);
        Assert.True(dto.IsActive);
    }

    [Fact]
    public void Chat_ToDto_MapsAllFields()
    {
        var chat = new Chat { Name = "General", CreatedById = Guid.NewGuid() };

        var dto = chat.ToDto();

        Assert.Equal(chat.Id, dto.Id);
        Assert.Equal("General", dto.Name);
        Assert.Equal(chat.CreatedById, dto.CreatedById);
    }

    [Fact]
    public void Chat_ToDetailDto_IncludesMembers()
    {
        var chat = new Chat { Name = "General", CreatedById = Guid.NewGuid() };
        var members = new List<NeuralDamage.Infrastructure.Dtos.ChatMemberDto>
        {
            new(Guid.NewGuid(), chat.Id, Guid.NewGuid(), null, "Owner", DateTime.UtcNow, null, null)
        };

        var dto = chat.ToDetailDto(members);

        Assert.Single(dto.Members);
        Assert.Equal("General", dto.Name);
    }

    [Fact]
    public void ChatMember_ToDto_MapsUserMember()
    {
        var user = new User { ExternalId = "ext-1", Email = "test@test.com", DisplayName = "Test User" };
        var member = new ChatMember { ChatId = Guid.NewGuid(), UserId = Guid.NewGuid(), Role = ChatMemberRole.Owner, User = user };

        var dto = member.ToDto();

        Assert.Equal("Owner", dto.Role);
        Assert.NotNull(dto.User);
        Assert.Null(dto.Bot);
        Assert.Equal("Test User", dto.User!.DisplayName);
    }

    [Fact]
    public void ChatMember_ToDto_MapsBotMember()
    {
        var bot = new Bot { Name = "GPT", ModelId = "openai/gpt-4o", CreatedById = Guid.NewGuid() };
        var member = new ChatMember { ChatId = Guid.NewGuid(), BotId = Guid.NewGuid(), Role = ChatMemberRole.Member, Bot = bot };

        var dto = member.ToDto();

        Assert.Equal("Member", dto.Role);
        Assert.Null(dto.User);
        Assert.NotNull(dto.Bot);
        Assert.Equal("GPT", dto.Bot!.Name);
    }

    [Fact]
    public void Message_ToDto_MapsWithSender()
    {
        var user = new User { ExternalId = "ext-1", Email = "test@test.com", DisplayName = "Tester" };
        var message = new Message { ChatId = Guid.NewGuid(), SenderUserId = Guid.NewGuid(), Content = "Hello world", Mentions = "[\"bot-1\"]", SenderUser = user };

        var dto = message.ToDto();

        Assert.Equal("Hello world", dto.Content);
        Assert.NotNull(dto.Mentions);
        Assert.Single(dto.Mentions!);
        Assert.Equal("bot-1", dto.Mentions![0]);
        Assert.NotNull(dto.SenderUser);
        Assert.Equal("Tester", dto.SenderUser!.DisplayName);
    }

    [Fact]
    public void Message_ToDto_NullMentions_ReturnsNull()
    {
        var message = new Message { ChatId = Guid.NewGuid(), Content = "Hi" };

        var dto = message.ToDto();

        Assert.Null(dto.Mentions);
    }

    [Fact]
    public void Reaction_ToDto_MapsAllFields()
    {
        var reaction = new Reaction { MessageId = Guid.NewGuid(), UserId = Guid.NewGuid(), Emoji = "🔥" };

        var dto = reaction.ToDto();

        Assert.Equal(reaction.MessageId, dto.MessageId);
        Assert.Equal("🔥", dto.Emoji);
        Assert.NotNull(dto.UserId);
    }

    [Fact]
    public void User_ToDto_MapsCorrectly()
    {
        var user = new User { ExternalId = "ext-1", Email = "test@test.com", DisplayName = "Tester", AvatarUrl = "https://example.com/pic.jpg" };

        var dto = user.ToDto();

        Assert.Equal(user.Id, dto.Id);
        Assert.Equal("test@test.com", dto.Email);
        Assert.Equal("Tester", dto.DisplayName);
        Assert.Equal("https://example.com/pic.jpg", dto.AvatarUrl);
    }
}
