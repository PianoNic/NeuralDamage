using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NeuralDamage.Domain;
using NeuralDamage.Infrastructure;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.Attachments;
using NeuralDamage.Tests.Helpers;
using NSubstitute;

namespace NeuralDamage.Tests.Services;

public class ImageInspectorTests
{
    [Test]
    public async Task ReadsTypeAndSize_OfEveryAllowedFormat()
    {
        await Assert.That(ImageInspector.Inspect(TestImages.Png(640, 480))).IsEqualTo(new ImageInfo("image/png", 640, 480));
        await Assert.That(ImageInspector.Inspect(TestImages.Gif(12, 34))).IsEqualTo(new ImageInfo("image/gif", 12, 34));
        await Assert.That(ImageInspector.Inspect(TestImages.Jpeg(1920, 1080))).IsEqualTo(new ImageInfo("image/jpeg", 1920, 1080));
        await Assert.That(ImageInspector.Inspect(TestImages.WebP(300, 200))).IsEqualTo(new ImageInfo("image/webp", 300, 200));
    }

    [Test]
    [Arguments("<svg xmlns=\"http://www.w3.org/2000/svg\"/>")]
    [Arguments("%PDF-1.7")]
    [Arguments("")]
    public async Task RefusesAnythingElse(string content)
    {
        await Assert.That(ImageInspector.Inspect(System.Text.Encoding.UTF8.GetBytes(content))).IsNull();
    }
}

public class ImageDescriberTests
{
    private sealed record Setup(ImageDescriber Describer, IOpenRouterService OpenRouter, IChatNotificationService Notifications, DbContextOptions<NeuralDamageDbContext> DbOptions, Attachment Attachment, ServiceProvider Provider) : IDisposable
    {
        public NeuralDamageDbContext Db() => new(DbOptions);
        public void Dispose() => Provider.Dispose();
    }

    private static async Task<Setup> CreateAsync(string? visionModel = null, bool sent = false)
    {
        var dbOptions = new DbContextOptionsBuilder<NeuralDamageDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var user = new User { ExternalId = "ext-1", Email = "alice@test.com", DisplayName = "Alice" };
        var chat = new Chat { Name = "Chat", CreatedById = user.Id };
        var message = new Message { ChatId = chat.Id, SenderUserId = user.Id, Content = "look" };
        var attachment = new Attachment { ChatId = chat.Id, UploaderUserId = user.Id, ContentType = "image/png", SizeBytes = 64, MessageId = sent ? message.Id : null };
        await using (var db = new NeuralDamageDbContext(dbOptions))
        {
            db.AddRange(user, chat, message, attachment);
            await db.SaveChangesAsync();
        }

        var storage = new InMemoryAttachmentStorage();
        await storage.SaveAsync(chat.Id, attachment.Id, TestImages.Png());

        var openRouter = Substitute.For<IOpenRouterService>();
        openRouter.ListModelsAsync(Arg.Any<CancellationToken>()).Returns([
            new OpenRouterModel(ImageDescriber.DefaultModel, "Gemma 3 12B", 131072, new ModelPricing(0.05m, 0.15m)) { Capabilities = [ModelMetadata.Vision] },
        ]);
        openRouter.ListZdrModelIdsAsync(Arg.Any<CancellationToken>()).Returns(new HashSet<string> { ImageDescriber.DefaultModel });
        openRouter.GenerateResponseAsync(Arg.Any<string>(), Arg.Any<double>(), Arg.Any<string>(), Arg.Any<List<ChatMessage>>(), Arg.Any<CancellationToken>())
            .Returns(Task.Delay(50).ContinueWith(_ => "A tabby cat asleep on a laptop keyboard."));

        var services = new ServiceCollection();
        services.AddScoped(_ => new NeuralDamageDbContext(dbOptions));
        services.AddSingleton<IAttachmentStorage>(storage);
        services.AddSingleton(openRouter);
        var notifications = Substitute.For<IChatNotificationService>();
        services.AddSingleton(notifications);
        services.AddSingleton(new ModelPolicy(ModelPolicy.DefaultMaxPromptPrice, ModelPolicy.DefaultMaxCompletionPrice, ZdrOnly: true));
        var provider = services.BuildServiceProvider();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["OpenRouter:VisionModel"] = visionModel })
            .Build();
        var describer = new ImageDescriber(provider.GetRequiredService<IServiceScopeFactory>(), configuration, NullLogger<ImageDescriber>.Instance);
        return new Setup(describer, openRouter, notifications, dbOptions, attachment, provider);
    }

    [Test]
    public async Task DescribesEachImageOnce_AndEveryoneWaitingGetsThatOne()
    {
        using var s = await CreateAsync();

        s.Describer.Enqueue(s.Attachment.Id);
        await Task.WhenAll(
            s.Describer.WaitAsync([s.Attachment.Id], TimeSpan.FromSeconds(10)),
            s.Describer.WaitAsync([s.Attachment.Id], TimeSpan.FromSeconds(10)));
        s.Describer.Enqueue(s.Attachment.Id);
        await s.Describer.WaitAsync([s.Attachment.Id], TimeSpan.FromSeconds(10));

        await s.OpenRouter.Received(1).GenerateResponseAsync(
            ImageDescriber.DefaultModel, Arg.Any<double>(), ImageDescriber.Prompt,
            Arg.Is<List<ChatMessage>>(h => h.Single().Images.Single().ContentType == "image/png"), Arg.Any<CancellationToken>());
        await using var db = s.Db();
        await Assert.That((await db.Attachments.SingleAsync()).Description).IsEqualTo("A tabby cat asleep on a laptop keyboard.");
    }

    [Test]
    public async Task ADescriptionOnASentMessage_IsPushedToTheChat()
    {
        using var s = await CreateAsync(sent: true);

        await s.Describer.WaitAsync([s.Attachment.Id], TimeSpan.FromSeconds(10));

        await s.Notifications.Received(1).NotifyAttachmentDescribed(
            s.Attachment.ChatId, s.Attachment.MessageId!.Value, s.Attachment.Id, "A tabby cat asleep on a laptop keyboard.");
    }

    [Test]
    public async Task ADescriptionOfAnUnsentUpload_IsNotBroadcast()
    {
        using var s = await CreateAsync();

        await s.Describer.WaitAsync([s.Attachment.Id], TimeSpan.FromSeconds(10));

        await using var db = s.Db();
        await Assert.That((await db.Attachments.SingleAsync()).Description).IsNotNull();
        await s.Notifications.DidNotReceiveWithAnyArgs().NotifyAttachmentDescribed(default, default, default, default!);
    }

    [Test]
    public async Task AStoredDescription_IsReused_EvenByANewProcess()
    {
        using var s = await CreateAsync();
        await s.Describer.WaitAsync([s.Attachment.Id], TimeSpan.FromSeconds(10));

        // A second describer is what a restart looks like.
        var again = new ImageDescriber(s.Provider.GetRequiredService<IServiceScopeFactory>(), new ConfigurationBuilder().Build(), NullLogger<ImageDescriber>.Instance);
        await again.WaitAsync([s.Attachment.Id], TimeSpan.FromSeconds(10));

        await s.OpenRouter.ReceivedWithAnyArgs(1).GenerateResponseAsync(default!, default, default!, default!, default);
    }

    [Test]
    [Arguments("google/gemma-3-12b-it:free")]
    [Arguments("openai/gpt-5")]
    public async Task NeverUsesAFreeVariant_OrAModelThePolicyRefuses(string model)
    {
        using var s = await CreateAsync(model);

        await s.Describer.WaitAsync([s.Attachment.Id], TimeSpan.FromSeconds(10));

        await s.OpenRouter.DidNotReceiveWithAnyArgs().GenerateResponseAsync(default!, default, default!, default!, default);
        await using var db = s.Db();
        await Assert.That((await db.Attachments.SingleAsync()).Description).IsNull();
    }

    [Test]
    public async Task WaitingGivesUp_AfterTheTimeout()
    {
        using var s = await CreateAsync();
        s.OpenRouter.GenerateResponseAsync(Arg.Any<string>(), Arg.Any<double>(), Arg.Any<string>(), Arg.Any<List<ChatMessage>>(), Arg.Any<CancellationToken>())
            .Returns(Task.Delay(TimeSpan.FromSeconds(30)).ContinueWith(_ => "late"));

        var started = DateTime.UtcNow;
        await s.Describer.WaitAsync([s.Attachment.Id], TimeSpan.FromMilliseconds(200));

        await Assert.That(DateTime.UtcNow - started).IsLessThan(TimeSpan.FromSeconds(5));
    }
}
