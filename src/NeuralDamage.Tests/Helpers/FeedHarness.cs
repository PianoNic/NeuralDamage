using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NeuralDamage.Domain;
using NeuralDamage.Infrastructure;
using NeuralDamage.Infrastructure.BackgroundServices;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.Attachments;
using NeuralDamage.Infrastructure.Services.Feed;
using NeuralDamage.Infrastructure.Services.Images;
using NSubstitute;

namespace NeuralDamage.Tests.Helpers;

/// <summary>
/// One person and a few public bots on the feed, wired to the real feed
/// conversation, post writer and scheduler over one in-memory database, with
/// Jev faked and the model substituted.
/// </summary>
public sealed class FeedHarness : IDisposable
{
    /// <summary>One renderer for every test: it loads the fonts once.</summary>
    public static readonly NeuralDamage.Infrastructure.Services.Stories.StoryRenderer SharedRenderer = new();

    public required NeuralDamageDbContext Db { get; init; }
    public required IFeedNotifier Notifier { get; init; }
    public required IOpenRouterService OpenRouter { get; init; }
    public required FakeJev Jev { get; init; }
    public required RecordingFeedQueue Queue { get; init; }
    public required FeedConversation Conversation { get; init; }
    public required FeedScheduler Scheduler { get; init; }
    public required ServiceProvider Provider { get; init; }
    public required User User { get; init; }
    public required List<Bot> Bots { get; init; }
    public required InMemoryAttachmentStorage Storage { get; init; }

    /// <summary>The picture maker, when the harness was made with a renderer.</summary>
    public ImageGenerator? Images { get; init; }

    public static async Task<FeedHarness> CreateAsync(int botCount = 2, FakeJev? jev = null, FeedOptions? feed = null, BotBehaviorOptions? behavior = null, BotClock? clock = null, TimeProvider? time = null, string? defaultModel = "gateway:free", IImageRenderer? renderer = null, ImageGenOptions? imageGen = null)
    {
        var dbOptions = new DbContextOptionsBuilder<NeuralDamageDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var db = new NeuralDamageDbContext(dbOptions);
        var user = new User { ExternalId = "ext-1", Email = "owner@example.com", DisplayName = "Alice" };
        db.Users.Add(user);
        string[] names = ["Gordon", "Kim", "Beast", "Uncle"];
        var bots = names.Take(botCount)
            .Select(n => new Bot { Name = n, ModelId = $"model/{n.ToLowerInvariant()}", SystemPrompt = $"You are {n}, a test character.", Personality = $"{n} the tester", CreatedById = user.Id })
            .ToList();
        db.Bots.AddRange(bots);
        await db.SaveChangesAsync();

        jev ??= new FakeJev();
        var notifier = Substitute.For<IFeedNotifier>();
        var openRouter = Substitute.For<IOpenRouterService>();
        var queue = new RecordingFeedQueue();
        var behaviorOptions = Options.Create(behavior ?? InstantBotOptions.Create().Value);
        var feedOptions = Options.Create(feed ?? new FeedOptions());
        clock ??= new BotClock(TimeZoneInfo.Utc);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["App:DefaultModel"] = defaultModel })
            .Build();

        var services = new ServiceCollection();
        services.AddScoped(_ => new NeuralDamageDbContext(dbOptions));
        services.AddSingleton(notifier);
        services.AddSingleton(openRouter);
        services.AddSingleton<IFeedQueue>(queue);
        services.AddSingleton(behaviorOptions);
        services.AddScoped(sp => new FeedPostWriter(sp.GetRequiredService<NeuralDamageDbContext>(), openRouter, configuration,
            NullLogger<FeedPostWriter>.Instance, feedOptions, clock));
        var storage = new InMemoryAttachmentStorage();
        services.AddSingleton<IAttachmentStorage>(storage);
        services.AddSingleton(Substitute.For<NeuralDamage.Infrastructure.Services.BotPictures.IBotPictureFinder>());
        services.AddSingleton(SharedRenderer);
        services.AddScoped(sp => new NeuralDamage.Infrastructure.Services.Stories.StoryPublisher(
            sp.GetRequiredService<NeuralDamageDbContext>(), sp.GetRequiredService<NeuralDamage.Infrastructure.Services.Attachments.IAttachmentStorage>(),
            SharedRenderer, sp.GetRequiredService<NeuralDamage.Infrastructure.Services.BotPictures.IBotPictureFinder>(), notifier,
            NullLogger<NeuralDamage.Infrastructure.Services.Stories.StoryPublisher>.Instance));
        services.AddScoped(sp => new NeuralDamage.Infrastructure.Services.Stories.StoryWriter(
            sp.GetRequiredService<NeuralDamageDbContext>(), openRouter, sp.GetRequiredService<FeedPostWriter>(),
            sp.GetRequiredService<NeuralDamage.Infrastructure.Services.Stories.StoryPublisher>(),
            NullLogger<NeuralDamage.Infrastructure.Services.Stories.StoryWriter>.Instance, clock, sp.GetService<ImageGenerator>()));
        jev.Engine()(services);
        if (renderer is not null)
        {
            var imageOptions = Options.Create(imageGen ?? new ImageGenOptions { BaseUrl = "http://comfyui.example.com/" });
            services.AddSingleton(renderer);
            services.AddSingleton(new ImageQueue(imageOptions));
            services.AddSingleton(sp => new ImageGenerator(sp.GetRequiredService<IServiceScopeFactory>(), sp.GetRequiredService<ImageQueue>(),
                storage, imageOptions, NullLogger<ImageGenerator>.Instance, clock, time));
        }
        var provider = services.BuildServiceProvider();
        var scopes = provider.GetRequiredService<IServiceScopeFactory>();

        return new FeedHarness
        {
            Db = db,
            Notifier = notifier,
            OpenRouter = openRouter,
            Jev = jev,
            Queue = queue,
            Conversation = new FeedConversation(scopes, NullLogger<FeedConversation>.Instance, behaviorOptions, feedOptions, clock),
            Scheduler = new FeedScheduler(scopes, NullLogger<FeedScheduler>.Instance, feedOptions, clock, time),
            Provider = provider,
            User = user,
            Bots = bots,
            Storage = storage,
            Images = provider.GetService<ImageGenerator>(),
        };
    }

    public void Reply(params string[] replies) =>
        OpenRouter.GenerateResponseAsync(default!, default, default!, default!, default).ReturnsForAnyArgs(replies[0], replies[1..]);

    public async Task<Post> PostAsync(string content, Bot? asBot = null, DateTime? at = null)
    {
        var post = new Post { AuthorUserId = asBot is null ? User.Id : null, AuthorBotId = asBot?.Id, Content = content, CreatedAt = at ?? DateTime.UtcNow };
        Db.Posts.Add(post);
        await Db.SaveChangesAsync();
        return post;
    }

    public async Task<PostComment> CommentAsync(Post post, string content, Bot? asBot = null, Guid? parentId = null, DateTime? at = null)
    {
        var comment = new PostComment { PostId = post.Id, ParentId = parentId, AuthorUserId = asBot is null ? User.Id : null, AuthorBotId = asBot?.Id, Content = content, CreatedAt = at ?? DateTime.UtcNow };
        Db.PostComments.Add(comment);
        await Db.SaveChangesAsync();
        return comment;
    }

    public Task<List<PostComment>> CommentsByAsync(Bot bot) =>
        Db.PostComments.AsNoTracking().Where(c => c.AuthorBotId == bot.Id).OrderBy(c => c.CreatedAt).ToListAsync();

    public void Dispose()
    {
        Provider.Dispose();
        Db.Dispose();
    }
}

public sealed class RecordingFeedQueue : IFeedQueue
{
    public List<FeedEvent> Items { get; } = [];

    public ValueTask EnqueueAsync(FeedEvent item, CancellationToken ct = default)
    {
        lock (Items)
            Items.Add(item);
        return ValueTask.CompletedTask;
    }
}

/// <summary>A clock that stands still where the test puts it.</summary>
public sealed class FixedTime(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;
    public override DateTimeOffset GetUtcNow() => Now;
}
