using NeuralDamage.Infrastructure.Mappers;
using NeuralDamage.Domain;
using NeuralDamage.Domain.Enums;

namespace NeuralDamage.Tests.Application;

public class MapperTests
{
    [Test]
    public async Task Bot_ToDto_MapsAllFields()
    {
        var bot = new Bot { Name = "TestBot", ModelId = "openai/gpt-4o", SystemPrompt = "Be helpful", Personality = "Friendly", Temperature = 0.9, AvatarUrl = "https://example.com/avatar.png", Aliases = "tb,test", CreatedById = Guid.NewGuid(), IsActive = true };

        var dto = bot.ToDto();

        await Assert.That(dto.Id).IsEqualTo(bot.Id);
        await Assert.That(dto.Name).IsEqualTo("TestBot");
        await Assert.That(dto.ModelId).IsEqualTo("openai/gpt-4o");
        await Assert.That(dto.SystemPrompt).IsEqualTo("Be helpful");
        await Assert.That(dto.Personality).IsEqualTo("Friendly");
        await Assert.That(dto.Temperature).IsEqualTo(0.9);
        await Assert.That(dto.AvatarUrl).IsEqualTo("https://example.com/avatar.png");
        await Assert.That(dto.Aliases).IsEqualTo("tb,test");
        await Assert.That(dto.IsActive).IsTrue();
    }

    [Test]
    public async Task Bot_ToSummaryDto_MapsCorrectFields()
    {
        var bot = new Bot { Name = "TestBot", ModelId = "test", CreatedById = Guid.NewGuid() };

        var dto = bot.ToSummaryDto();

        await Assert.That(dto.Id).IsEqualTo(bot.Id);
        await Assert.That(dto.Name).IsEqualTo("TestBot");
        await Assert.That(dto.IsActive).IsTrue();
    }

    [Test]
    public async Task Chat_ToDto_MapsAllFields()
    {
        var chat = new Chat { Name = "General", CreatedById = Guid.NewGuid() };

        var dto = chat.ToDto();

        await Assert.That(dto.Id).IsEqualTo(chat.Id);
        await Assert.That(dto.Name).IsEqualTo("General");
        await Assert.That(dto.CreatedById).IsEqualTo(chat.CreatedById);
    }

    [Test]
    public async Task Chat_ToDetailDto_IncludesMembers()
    {
        var chat = new Chat { Name = "General", CreatedById = Guid.NewGuid() };
        var members = new List<NeuralDamage.Infrastructure.Dtos.ChatMemberDto>
        {
            new(Guid.NewGuid(), chat.Id, Guid.NewGuid(), null, "Owner", DateTime.UtcNow, null, null)
        };

        var dto = chat.ToDetailDto(members);

        await Assert.That(dto.Members).HasSingleItem();
        await Assert.That(dto.Name).IsEqualTo("General");
    }

    [Test]
    public async Task ChatMember_ToDto_MapsUserMember()
    {
        var user = new User { ExternalId = "ext-1", Email = "test@test.com", DisplayName = "Test User" };
        var member = new ChatMember { ChatId = Guid.NewGuid(), UserId = Guid.NewGuid(), Role = ChatMemberRole.Owner, User = user };

        var dto = member.ToDto();

        await Assert.That(dto.Role).IsEqualTo("Owner");
        await Assert.That(dto.User).IsNotNull();
        await Assert.That(dto.Bot).IsNull();
        await Assert.That(dto.User!.DisplayName).IsEqualTo("Test User");
    }

    [Test]
    public async Task ChatMember_ToDto_MapsBotMember()
    {
        var bot = new Bot { Name = "GPT", ModelId = "openai/gpt-4o", CreatedById = Guid.NewGuid() };
        var member = new ChatMember { ChatId = Guid.NewGuid(), BotId = Guid.NewGuid(), Role = ChatMemberRole.Member, Bot = bot };

        var dto = member.ToDto();

        await Assert.That(dto.Role).IsEqualTo("Member");
        await Assert.That(dto.User).IsNull();
        await Assert.That(dto.Bot).IsNotNull();
        await Assert.That(dto.Bot!.Name).IsEqualTo("GPT");
    }

    [Test]
    public async Task Message_ToDto_MapsWithSender()
    {
        var user = new User { ExternalId = "ext-1", Email = "test@test.com", DisplayName = "Tester" };
        var message = new Message { ChatId = Guid.NewGuid(), SenderUserId = Guid.NewGuid(), Content = "Hello world", Mentions = "[\"bot-1\"]", SenderUser = user };

        var dto = message.ToDto();

        await Assert.That(dto.Content).IsEqualTo("Hello world");
        await Assert.That(dto.Mentions).IsNotNull();
        await Assert.That(dto.Mentions!).HasSingleItem();
        await Assert.That(dto.Mentions![0]).IsEqualTo("bot-1");
        await Assert.That(dto.SenderUser).IsNotNull();
        await Assert.That(dto.SenderUser!.DisplayName).IsEqualTo("Tester");
    }

    [Test]
    public async Task Message_ToDto_NullMentions_ReturnsNull()
    {
        var message = new Message { ChatId = Guid.NewGuid(), Content = "Hi" };

        var dto = message.ToDto();

        await Assert.That(dto.Mentions).IsNull();
    }

    [Test]
    public async Task Reaction_ToDto_MapsAllFields()
    {
        var reaction = new Reaction { MessageId = Guid.NewGuid(), UserId = Guid.NewGuid(), Emoji = "🔥" };

        var dto = reaction.ToDto();

        await Assert.That(dto.MessageId).IsEqualTo(reaction.MessageId);
        await Assert.That(dto.Emoji).IsEqualTo("🔥");
        await Assert.That(dto.UserId).IsNotNull();
    }

    [Test]
    public async Task User_ToDto_MapsCorrectly()
    {
        var user = new User { ExternalId = "ext-1", Email = "test@test.com", DisplayName = "Tester", AvatarUrl = "https://example.com/pic.jpg" };

        var dto = user.ToDto();

        await Assert.That(dto.Id).IsEqualTo(user.Id);
        await Assert.That(dto.Email).IsEqualTo("test@test.com");
        await Assert.That(dto.DisplayName).IsEqualTo("Tester");
        await Assert.That(dto.AvatarUrl).IsEqualTo("https://example.com/pic.jpg");
    }
}
