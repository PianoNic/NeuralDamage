using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NeuralDamage.Application.Commands;
using NeuralDamage.Application.Queries;
using NeuralDamage.Domain;
using NeuralDamage.Domain.Enums;
using NeuralDamage.Infrastructure;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.Attachments;
using NeuralDamage.Infrastructure.Services.Feed;
using NeuralDamage.Tests.Helpers;
using NSubstitute;
using Toamaisutaa.Abstractions;

namespace NeuralDamage.Tests.Commands;

public class AdminTests
{
    private static ClaimsPrincipal Principal(params Claim[] claims) => new(new ClaimsIdentity(claims, "test"));

    [Test]
    [Arguments(ClaimTypes.Role)]
    [Arguments("role")]
    [Arguments("roles")]
    [Arguments("groups")]
    public async Task TheAdminRole_CountsInEveryRoleClaim(string type)
    {
        var admins = new AppAdmins([]);

        await Assert.That(admins.HasAdminRole(Principal(new Claim(type, "private")))).IsTrue();
        await Assert.That(admins.HasAdminRole(Principal(new Claim(type, "PRIVATE")))).IsTrue();
        await Assert.That(admins.HasAdminRole(Principal(new Claim(type, "everyone")))).IsFalse();
    }

    [Test]
    public async Task TheAdminRole_IsFoundAmongSeveral_OrInAListHandedOverAsOneValue()
    {
        var admins = new AppAdmins([]);

        await Assert.That(admins.HasAdminRole(Principal(new Claim("groups", "everyone"), new Claim("groups", "Private")))).IsTrue();
        await Assert.That(admins.HasAdminRole(Principal(new Claim("groups", """["everyone","private"]""")))).IsTrue();
        await Assert.That(admins.HasAdminRole(Principal(new Claim("roles", """{"private":{"id":"1"}}""")))).IsTrue();
        await Assert.That(admins.HasAdminRole(Principal(new Claim("groups", """["everyone"]""")))).IsFalse();
    }

    [Test]
    public async Task NoRole_OrTheRoleInAnotherClaim_IsNoAdmin()
    {
        var admins = new AppAdmins([]);

        await Assert.That(admins.HasAdminRole(null)).IsFalse();
        await Assert.That(admins.HasAdminRole(Principal())).IsFalse();
        await Assert.That(admins.HasAdminRole(Principal(new Claim("scope", "private")))).IsFalse();
    }

    [Test]
    public async Task TheAdminRole_IsConfigurable_AndTheEmailsStillCount()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["App:AdminRole"] = "moderators",
            ["App:Admins"] = "boss@example.com",
        }).Build();
        var admins = AppAdmins.FromConfiguration(config);

        await Assert.That(admins.Role).IsEqualTo("moderators");
        await Assert.That(admins.IsAdmin(Principal(new Claim("groups", "moderators")), "someone@example.com")).IsTrue();
        await Assert.That(admins.IsAdmin(Principal(new Claim("groups", AppAdmins.DefaultRole)), "someone@example.com")).IsFalse();
        await Assert.That(admins.IsAdmin(Principal(), "Boss@example.com")).IsTrue();
        await Assert.That(AppAdmins.FromConfiguration(new ConfigurationBuilder().Build()).Role).IsEqualTo(AppAdmins.DefaultRole);
    }

    [Test]
    public async Task SigningIn_StoresWhetherTheUserIsAnAdmin_AndForgetsItWhenTheRoleGoes()
    {
        // The new user is saved through a context of its own, so the store is shared explicitly.
        var options = new DbContextOptionsBuilder<NeuralDamageDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString(), new Microsoft.EntityFrameworkCore.Storage.InMemoryDatabaseRoot()).Options;
        using var db = new NeuralDamageDbContext(options);
        var current = Substitute.For<ICurrentUser>();
        current.Subject.Returns("ext-9");
        var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        var service = new UserService(current, http, db, options, new AppAdmins([]), NullLogger<UserService>.Instance);

        http.HttpContext.User = Principal(new Claim("sub", "ext-9"), new Claim("email", "a@example.com"), new Claim("groups", "private"));
        await service.SyncCurrentUserAsync();
        await using (var check = new NeuralDamageDbContext(options))
            await Assert.That((await check.Users.SingleAsync()).IsAdmin).IsTrue();

        http.HttpContext.User = Principal(new Claim("sub", "ext-9"), new Claim("email", "a@example.com"));
        await service.SyncCurrentUserAsync();
        await Assert.That((await db.Users.SingleAsync()).IsAdmin).IsFalse();
    }

    private sealed record Setup(NeuralDamageDbContext Db, User Owner, User Other, User Admin, Bot Bot, Chat Chat) : IDisposable
    {
        public void Dispose() => Db.Dispose();
    }

    /// <summary>The owner's private bot in the owner's chat, someone else, and an admin who is in neither.</summary>
    private static async Task<Setup> CreateAsync()
    {
        var db = TestDbContext.Create();
        var owner = new User { ExternalId = "ext-1", Email = "owner@example.com", DisplayName = "Owner" };
        var other = new User { ExternalId = "ext-2", Email = "other@example.com", DisplayName = "Other" };
        var admin = new User { ExternalId = "ext-3", Email = "admin@example.com", DisplayName = "Admin", IsAdmin = true };
        var chat = new Chat { Name = "Theirs", CreatedById = owner.Id };
        var bot = new Bot { Name = "Secret", ModelId = "cheap/model", SystemPrompt = "Old", CreatedById = owner.Id, IsPublic = false, ChatId = chat.Id, Looks = "tall" };
        db.Users.AddRange(owner, other, admin);
        db.Chats.Add(chat);
        db.Bots.Add(bot);
        db.ChatMembers.AddRange(
            new ChatMember { ChatId = chat.Id, UserId = owner.Id, Role = ChatMemberRole.Owner },
            new ChatMember { ChatId = chat.Id, UserId = other.Id },
            new ChatMember { ChatId = chat.Id, BotId = bot.Id });
        await db.SaveChangesAsync();
        return new Setup(db, owner, other, admin, bot, chat);
    }

    [Test]
    public async Task AnAdmin_EditsSomeoneElsesPrivateBot_ButNobodyElseDoes()
    {
        using var s = await CreateAsync();
        var handler = new UpdateBotHandler(s.Db, Substitute.For<IOpenRouterService>(), new ModelPolicy(0, 0), new ChatBotState());

        var refused = await handler.Handle(new UpdateBotCommand(s.Bot.Id, s.Other.Id, "Hacked", null, null, null, null, null, null), default);
        var edited = await handler.Handle(new UpdateBotCommand(s.Bot.Id, s.Admin.Id, "Renamed", null, "New", null, null, null, null), default);

        await Assert.That(refused.IsSuccess).IsFalse();
        await Assert.That(edited.IsSuccess).IsTrue();
        await Assert.That(s.Bot.Name).IsEqualTo("Renamed");
        await Assert.That(s.Bot.SystemPrompt).IsEqualTo("New");
    }

    [Test]
    public async Task AnAdmin_DeletesSomeoneElsesBot_ButNobodyElseDoes()
    {
        using var s = await CreateAsync();
        var handler = new DeleteBotHandler(s.Db, Substitute.For<IChatNotificationService>(), new InMemoryAttachmentStorage());

        var refused = await handler.Handle(new DeleteBotCommand(s.Bot.Id, s.Other.Id), default);
        await Assert.That(refused.IsSuccess).IsFalse();
        await Assert.That(s.Bot.IsActive).IsTrue();

        var deleted = await handler.Handle(new DeleteBotCommand(s.Bot.Id, s.Admin.Id), default);
        await Assert.That(deleted.IsSuccess).IsTrue();
        await Assert.That(s.Bot.IsActive).IsFalse();
    }

    [Test]
    public async Task AnAdmin_SeesSomeoneElsesPrivateBot_WithItsLooks()
    {
        using var s = await CreateAsync();
        var outsider = new User { ExternalId = "ext-4", Email = "out@example.com", DisplayName = "Out" };
        s.Db.Users.Add(outsider);
        await s.Db.SaveChangesAsync();
        var openRouter = Substitute.For<IOpenRouterService>();
        openRouter.ListModelsAsync(Arg.Any<CancellationToken>()).Returns([]);
        var handler = new GetBotHandler(s.Db, openRouter, new ModelPolicy(0, 0));

        var hidden = await handler.Handle(new GetBotQuery(s.Bot.Id, outsider.Id), default);
        var seen = await handler.Handle(new GetBotQuery(s.Bot.Id, s.Admin.Id), default);

        await Assert.That(hidden.IsSuccess).IsFalse();
        await Assert.That(seen.IsSuccess).IsTrue();
        await Assert.That(seen.Value!.Looks).IsEqualTo("tall");
    }

    [Test]
    public async Task AnAdmin_RunsSomeoneElsesChat_ButAMemberDoesNot()
    {
        using var s = await CreateAsync();
        var handler = new UpdateChatHandler(s.Db, Substitute.For<IChatNotificationService>());

        var refused = await handler.Handle(new UpdateChatCommand(s.Chat.Id, "Mine now", s.Other.Id), default);
        var renamed = await handler.Handle(new UpdateChatCommand(s.Chat.Id, "Tidied", s.Admin.Id), default);

        await Assert.That(refused.IsSuccess).IsFalse();
        await Assert.That(renamed.IsSuccess).IsTrue();
        await Assert.That(s.Chat.Name).IsEqualTo("Tidied");
    }

    [Test]
    public async Task AnAdminByRole_DeletesSomeoneElsesPost()
    {
        using var h = await FeedHarness.CreateAsync(botCount: 1);
        var post = await h.PostAsync("mine");
        var other = new User { ExternalId = "ext-2", Email = "other@example.com", DisplayName = "Other" };
        var admin = new User { ExternalId = "ext-3", Email = "admin@example.com", DisplayName = "Admin", IsAdmin = true };
        h.Db.Users.AddRange(other, admin);
        await h.Db.SaveChangesAsync();
        var handler = new DeletePostHandler(h.Db, AppAdmins.None, h.Notifier, new InMemoryAttachmentStorage());

        var refused = await handler.Handle(new DeletePostCommand(other.Id, post.Id), default);
        var deleted = await handler.Handle(new DeletePostCommand(admin.Id, post.Id), default);

        await Assert.That(refused.IsSuccess).IsFalse();
        await Assert.That(deleted.IsSuccess).IsTrue();
    }

    [Test]
    public async Task OnlyAnAdmin_HasABotPostNow()
    {
        using var s = await CreateAsync();
        s.Bot.IsPublic = true;
        s.Bot.ChatId = null;
        var emailAdmin = new User { ExternalId = "ext-5", Email = "boss@example.com", DisplayName = "Boss" };
        s.Db.Users.Add(emailAdmin);
        await s.Db.SaveChangesAsync();
        var now = Substitute.For<IFeedNow>();
        var handler = new BotPostNowHandler(s.Db, new AppAdmins(["boss@example.com"]), now);

        var refused = await handler.Handle(new BotPostNowCommand(s.Bot.Id, s.Owner.Id), default);
        await Assert.That(refused.Error).IsEqualTo(BotPostNowHandler.AdminsOnly);
        now.DidNotReceiveWithAnyArgs().Start(default, default);

        var posted = await handler.Handle(new BotPostNowCommand(s.Bot.Id, s.Admin.Id), default);
        var story = await handler.Handle(new BotPostNowCommand(s.Bot.Id, emailAdmin.Id, Story: true), default);
        await Assert.That(posted.IsSuccess && story.IsSuccess).IsTrue();
        now.Received(1).Start(s.Bot.Id, false);
        now.Received(1).Start(s.Bot.Id, true);
    }

    [Test]
    public async Task APrivateBot_DoesNotPostOnTheFeed_EvenForAnAdmin()
    {
        using var s = await CreateAsync();
        var now = Substitute.For<IFeedNow>();

        var result = await new BotPostNowHandler(s.Db, AppAdmins.None, now).Handle(new BotPostNowCommand(s.Bot.Id, s.Admin.Id), default);

        await Assert.That(result.Error).IsEqualTo(BotPostNowHandler.NotFound);
        now.DidNotReceiveWithAnyArgs().Start(default, default);
    }

    [Test]
    public async Task PostingNow_WritesAPost_WhateverTheSchedule()
    {
        // Posting is switched off, and the bot already used up its posts for the day.
        using var h = await FeedHarness.CreateAsync(botCount: 1, feed: new FeedOptions { BotsPost = false, ChatInspiredShare = 0 });
        h.Bots[0].MaxPostsPerDay = 1;
        await h.Db.SaveChangesAsync();
        await h.PostAsync("already posted today", h.Bots[0]);
        h.Reply("fresh off the pass");

        await Assert.That(await h.Scheduler.NowAsync(Guid.NewGuid(), story: false)).IsFalse();
        await Assert.That(await h.Scheduler.NowAsync(h.Bots[0].Id, story: false, random: () => 0.99)).IsTrue();
        await h.Notifier.Received(1).PostNew(Arg.Is<NeuralDamage.Infrastructure.Dtos.PostDto>(p => p.Content == "fresh off the pass"));
    }
}
