using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NeuralDamage.Domain;
using NeuralDamage.Domain.Enums;
using NeuralDamage.Infrastructure;
using NeuralDamage.Infrastructure.Dtos;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Tests.Helpers;
using NSubstitute;

namespace NeuralDamage.Tests.Infrastructure;

public class BotDirectMessageTests
{
    private static readonly BotBehaviorOptions Options = new();

    private sealed class World : IDisposable
    {
        public required NeuralDamageDbContext Db { get; init; }
        public required IChatNotificationService Notifications { get; init; }
        public required User Mattia { get; init; }
        public required User Outsider { get; init; }
        public required Bot Mia { get; init; }
        public required Chat Group { get; init; }

        public static async Task<World> CreateAsync()
        {
            var db = TestDbContext.Create();
            var mattia = new User { ExternalId = "ext-1", Email = "mattia@example.com", DisplayName = "MyDrift", Nicknames = "Mattia" };
            var outsider = new User { ExternalId = "ext-2", Email = "out@example.com", DisplayName = "Outsider" };
            db.Users.AddRange(mattia, outsider);
            var mia = new Bot { Name = "Mia", ModelId = "m", SystemPrompt = "x", CreatedById = mattia.Id };
            db.Bots.Add(mia);
            var group = new Chat { Name = "fight", CreatedById = mattia.Id };
            db.Chats.Add(group);
            db.ChatMembers.AddRange(
                new ChatMember { ChatId = group.Id, UserId = mattia.Id, Role = ChatMemberRole.Owner },
                new ChatMember { ChatId = group.Id, BotId = mia.Id });
            await db.SaveChangesAsync();
            return new World { Db = db, Notifications = Substitute.For<IChatNotificationService>(), Mattia = mattia, Outsider = outsider, Mia = mia, Group = group };
        }

        public Task<Message?> SendAsync(string to, string text = "goodnight babe", DateTime? at = null) =>
            BotDirectMessages.SendAsync(Db, Notifications, Mia, Group, new DirectMessageAsk(to, text), Options, at ?? DateTime.UtcNow);

        public void Dispose() => Db.Dispose();
    }

    [Test]
    public async Task TheTag_LeavesTheGroupReply_AndSaysWhatToSendWhom()
    {
        var (said, ask) = BotDirectMessages.Extract("okay that's enough chat for tonight 🫡 [dm @MyDrift: i need a proper goodnight from you] [dm @Bob: second one]");

        await Assert.That(said).IsEqualTo("okay that's enough chat for tonight 🫡");
        await Assert.That(ask).IsEqualTo(new DirectMessageAsk("MyDrift", "i need a proper goodnight from you"));
        await Assert.That(BotDirectMessages.Extract("no tag here").Ask).IsNull();
        await Assert.That(BotDirectMessages.Extract("[DM to Mattia: hey]").Ask!.To).IsEqualTo("Mattia");
    }

    [Test]
    public async Task ABot_TextsSomeoneInTheChat_InTheirOneDirectChat()
    {
        using var w = await World.CreateAsync();

        var sent = await w.SendAsync("Mattia");

        await Assert.That(sent).IsNotNull();
        var direct = await w.Db.Chats.SingleAsync(c => c.DirectKey == Chat.DirectKeyOf(w.Mattia.Id, w.Mia.Id));
        await Assert.That(sent!.ChatId).IsEqualTo(direct.Id);
        await Assert.That(sent.Content).IsEqualTo("goodnight babe");
        await w.Notifications.Received(1).NotifyUserChatCreated(w.Mattia.Id, Arg.Any<ChatDto>());
        await w.Notifications.Received(1).NotifyUserMessageNew(w.Mattia.Id, Arg.Is<MessageDto>(m => m.ChatId == direct.Id));
    }

    [Test]
    public async Task ASecondText_WithinTheCooldown_IsNotSent_ButLaterItIs()
    {
        using var w = await World.CreateAsync();
        var now = DateTime.UtcNow;

        var first = await w.SendAsync("MyDrift", at: now);
        var tooSoon = await w.SendAsync("MyDrift", "you up?", now.AddMinutes(5));
        var later = await w.SendAsync("MyDrift", "morning", now + Options.DirectMessageCooldown + TimeSpan.FromMinutes(1));

        await Assert.That(first).IsNotNull();
        await Assert.That(tooSoon).IsNull();
        await Assert.That(later).IsNotNull();
        await Assert.That(await w.Db.Chats.CountAsync(c => c.DirectKey != null)).IsEqualTo(1);
    }

    [Test]
    public async Task NobodyOutsideTheChat_OrWhoTurnedItOff_IsTexted()
    {
        using var w = await World.CreateAsync();

        var outsider = await w.SendAsync("Outsider");
        w.Mattia.AllowBotDirectMessages = false;
        await w.Db.SaveChangesAsync();
        var refused = await w.SendAsync("Mattia");

        await Assert.That(outsider).IsNull();
        await Assert.That(refused).IsNull();
        await Assert.That(await w.Db.Messages.AnyAsync()).IsFalse();
    }

    [Test]
    public async Task APrivateBot_OrOneAlreadyInADirectChat_DoesNotText()
    {
        using var w = await World.CreateAsync();
        var direct = new Chat { Name = "Mia", CreatedById = w.Mattia.Id, DirectKey = Chat.DirectKeyOf(w.Mattia.Id, w.Mia.Id) };

        await Assert.That(BotDirectMessages.CanSendFrom(w.Mia, direct, Options)).IsFalse();
        w.Mia.IsPublic = false;
        await Assert.That(BotDirectMessages.CanSendFrom(w.Mia, w.Group, Options)).IsFalse();
        await Assert.That(BotDirectMessages.CanSendFrom(w.Mia, w.Group, new BotBehaviorOptions { DirectMessages = false })).IsFalse();
    }

    [Test]
    public async Task TheChatPrompt_OffersTexting_OnlyWhenTheBotMay()
    {
        var bot = new Bot { Name = "Mia", ModelId = "m", SystemPrompt = "x", CreatedById = Guid.NewGuid() };

        await Assert.That(BotPromptBuilder.BuildSystemPrompt(bot, ["MyDrift"], canTextDirectly: true)).Contains("[dm @TheirName: the message]");
        await Assert.That(BotPromptBuilder.BuildSystemPrompt(bot, ["MyDrift"])).DoesNotContain("[dm @");
    }

    [Test]
    public async Task ABotsReply_GoesToTheGroup_AndItsTextToTheDirectChat()
    {
        using var h = await OrchestratorHarness.CreateAsync();
        var bot = h.Bots[0];
        h.Respond(bot);
        h.Reply("okay goodnight chaos crew [dm @Alice: night babe, text me tomorrow]");

        var message = await h.SayAsync("are you leaving?");
        await h.Orchestrator.ProcessMessageAsync(h.Chat.Id, message.Id);

        var inGroup = await h.Db.Messages.AsNoTracking().Where(m => m.ChatId == h.Chat.Id && m.SenderBotId == bot.Id).Select(m => m.Content).ToListAsync();
        var direct = await h.Db.Chats.AsNoTracking().SingleAsync(c => c.DirectKey == Chat.DirectKeyOf(h.User.Id, bot.Id));
        var texted = await h.Db.Messages.AsNoTracking().SingleAsync(m => m.ChatId == direct.Id);
        await Assert.That(inGroup).IsEquivalentTo(new[] { "okay goodnight chaos crew" });
        await Assert.That(texted.Content).IsEqualTo("night babe, text me tomorrow");
    }

    [Test]
    public async Task TwoBotsAnswering_PostTheirSplitRepliesWhole_NeverInterleaved()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new BotBehaviorOptions
        {
            ReadDelayMin = TimeSpan.Zero,
            ReadDelayMax = TimeSpan.Zero,
            TypingMin = TimeSpan.FromMilliseconds(120),
            TypingMax = TimeSpan.FromMilliseconds(120),
            TypingHeartbeat = TimeSpan.FromMilliseconds(50),
            ReactionDelayMin = TimeSpan.Zero,
            ReactionDelayMax = TimeSpan.Zero,
        });
        using var h = await OrchestratorHarness.CreateAsync(botCount: 2, options: options);
        h.Respond(h.Bots[0], h.Bots[1]);
        h.Reply("okay fine. one joke\n\nwhy did the scarecrow win an award?", "why do i have to be the comedian now\n\nfine, here goes");

        var message = await h.SayAsync("tell me a joke");
        await h.Orchestrator.ProcessMessageAsync(h.Chat.Id, message.Id);

        var senders = await h.Db.Messages.AsNoTracking()
            .Where(m => m.ChatId == h.Chat.Id && m.SenderBotId != null)
            .OrderBy(m => m.CreatedAt)
            .Select(m => m.SenderBotId)
            .ToListAsync();
        await Assert.That(senders).Count().IsEqualTo(4);
        await Assert.That(senders[0]).IsEqualTo(senders[1]);
        await Assert.That(senders[2]).IsEqualTo(senders[3]);
        await Assert.That(senders[0]).IsNotEqualTo(senders[2]);
    }
}
