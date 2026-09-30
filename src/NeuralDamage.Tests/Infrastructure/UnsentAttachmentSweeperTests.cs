using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NeuralDamage.Domain;
using NeuralDamage.Infrastructure;
using NeuralDamage.Infrastructure.BackgroundServices;
using NeuralDamage.Infrastructure.Services.Attachments;
using NeuralDamage.Tests.Helpers;

namespace NeuralDamage.Tests.Infrastructure;

public class UnsentAttachmentSweeperTests
{
    [Test]
    public async Task SweepsOldUnsentUploads_KeepsSentAndRecentOnes()
    {
        var dbOptions = new DbContextOptionsBuilder<NeuralDamageDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var now = new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);
        var user = new User { ExternalId = "ext-1", Email = "alice@test.com", DisplayName = "Alice" };
        var chat = new Chat { Name = "Chat", CreatedById = user.Id };
        var message = new Message { ChatId = chat.Id, SenderUserId = user.Id, Content = "look" };
        Attachment Upload(TimeSpan age, Guid? messageId = null) => new()
        {
            ChatId = chat.Id, UploaderUserId = user.Id, ContentType = "image/png", SizeBytes = 64,
            CreatedAt = now - age, MessageId = messageId,
        };
        var abandoned = Upload(TimeSpan.FromHours(2));
        var recent = Upload(TimeSpan.FromMinutes(5));
        var sent = Upload(TimeSpan.FromDays(3), message.Id);

        var storage = new InMemoryAttachmentStorage();
        await using (var db = new NeuralDamageDbContext(dbOptions))
        {
            db.AddRange(user, chat, message, abandoned, recent, sent);
            await db.SaveChangesAsync();
        }
        foreach (var a in new[] { abandoned, recent, sent })
            await storage.SaveAsync(chat.Id, a.Id, TestImages.Png());

        var services = new ServiceCollection();
        services.AddScoped(_ => new NeuralDamageDbContext(dbOptions));
        await using var provider = services.BuildServiceProvider();
        var sweeper = new UnsentAttachmentSweeper(provider.GetRequiredService<IServiceScopeFactory>(), storage,
            new AttachmentOptions { UnsentMaxAge = TimeSpan.FromHours(1) }, NullLogger<UnsentAttachmentSweeper>.Instance);

        var swept = await sweeper.SweepAsync(now);

        await Assert.That(swept).IsEqualTo(1);
        await using var check = new NeuralDamageDbContext(dbOptions);
        await Assert.That(await check.Attachments.Select(a => a.Id).ToListAsync()).IsEquivalentTo([recent.Id, sent.Id]);
        await Assert.That(storage.Files.Keys.Select(k => k.Id)).IsEquivalentTo([recent.Id, sent.Id]);
        await Assert.That(await sweeper.SweepAsync(now)).IsEqualTo(0);
    }

    [Test]
    public async Task TheAgeLimit_IsConfigurable()
    {
        var configured = AttachmentOptions.FromConfiguration(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Attachments:UnsentMaxAgeMinutes"] = "15" })
            .Build());

        await Assert.That(configured.UnsentMaxAge).IsEqualTo(TimeSpan.FromMinutes(15));
        await Assert.That(new AttachmentOptions().UnsentMaxAge).IsEqualTo(TimeSpan.FromHours(1));
    }

    [Test]
    public async Task TheClientIsServedTheConfiguredLimits()
    {
        var options = AttachmentOptions.FromConfiguration(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Attachments:MaxBytes"] = "2097152",
                ["Attachments:MaxPerMessage"] = "2",
            })
            .Build());

        var limits = options.Limits();

        await Assert.That(limits.MaxBytes).IsEqualTo(2097152L);
        await Assert.That(limits.MaxPerMessage).IsEqualTo(2);
        await Assert.That(limits.ContentTypes).IsEquivalentTo(["image/png", "image/jpeg", "image/webp", "image/gif"]);
    }
}
