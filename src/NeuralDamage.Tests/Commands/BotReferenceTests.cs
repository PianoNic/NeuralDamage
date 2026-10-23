using Microsoft.EntityFrameworkCore;
using NeuralDamage.Application.Commands;
using NeuralDamage.Application.Queries;
using NeuralDamage.Application.Validators;
using NeuralDamage.Domain;
using NeuralDamage.Infrastructure;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.Attachments;
using NeuralDamage.Tests.Helpers;
using NSubstitute;
using SkiaSharp;

namespace NeuralDamage.Tests.Commands;

/// <summary>Writes looks without a vision model, and remembers what it was shown.</summary>
public sealed class FakeLooksDescriber : ILooksDescriber
{
    public List<int> Calls { get; } = [];
    public string? Answer { get; set; } = "Shoulder-length red hair, freckles, green hoodie.";

    public Task<string?> DescribeLooksAsync(IReadOnlyList<ImagePart> pictures, CancellationToken ct = default)
    {
        Calls.Add(pictures.Count);
        return Task.FromResult(Answer is null ? null : $"{Answer} ({pictures.Count})");
    }
}

/// <summary>A bot's reference pictures and looks: limits, who may touch them, and who sees them.</summary>
public class BotReferenceTests
{
    private sealed record World(NeuralDamageDbContext Db, User Owner, User Stranger, Bot Bot, InMemoryAttachmentStorage Storage, FakeLooksDescriber Describer) : IDisposable
    {
        public AddBotReferenceHandler Add(AttachmentOptions? options = null) => new(Db, Storage, options ?? new AttachmentOptions(), Describer);
        public void Dispose() => Db.Dispose();
    }

    private static async Task<World> SeedAsync()
    {
        var db = TestDbContext.Create();
        var owner = new User { ExternalId = "ext-1", Email = "owner@test.com", DisplayName = "Owner" };
        var stranger = new User { ExternalId = "ext-2", Email = "stranger@test.com", DisplayName = "Stranger" };
        var bot = new Bot { Name = "Mia", ModelId = "m/x", SystemPrompt = "You are Mia.", CreatedById = owner.Id };
        db.AddRange(owner, stranger, bot);
        await db.SaveChangesAsync();
        return new World(db, owner, stranger, bot, new InMemoryAttachmentStorage(), new FakeLooksDescriber());
    }

    private static readonly SKColor[] Hair = [SKColors.SaddleBrown, SKColors.Gold, SKColors.Black, SKColors.Red];

    [Test]
    public async Task TheCreator_AddsUpToThree_AndTheLooksAreWrittenFromAllOfThem()
    {
        using var w = await SeedAsync();

        for (var i = 0; i < BotReference.MaxPerBot; i++)
        {
            var added = await w.Add().Handle(new AddBotReferenceCommand(w.Bot.Id, w.Owner.Id, TestImages.Face(Hair[i])), CancellationToken.None);
            await Assert.That(added.IsSuccess).IsTrue();
            await Assert.That(added.Value!.References.Count).IsEqualTo(i + 1);
        }
        var fourth = await w.Add().Handle(new AddBotReferenceCommand(w.Bot.Id, w.Owner.Id, TestImages.Face(Hair[3])), CancellationToken.None);

        await Assert.That(fourth.IsFailure).IsTrue();
        await Assert.That(fourth.Error!).Contains("at most 3");
        await Assert.That(w.Storage.BotReferences.Count).IsEqualTo(3);
        await Assert.That(w.Describer.Calls).IsEquivalentTo(new[] { 1, 2, 3 });
        await Assert.That(w.Bot.Looks).IsEqualTo("Shoulder-length red hair, freckles, green hoodie. (3)");
    }

    [Test]
    public async Task AFailingDescriber_KeepsTheLooksAsTheyWere()
    {
        using var w = await SeedAsync();
        w.Bot.Looks = "Written by hand.";
        await w.Db.SaveChangesAsync();
        w.Describer.Answer = null;

        var added = await w.Add().Handle(new AddBotReferenceCommand(w.Bot.Id, w.Owner.Id, TestImages.Face(Hair[0])), CancellationToken.None);

        await Assert.That(added.IsSuccess).IsTrue();
        await Assert.That(added.Value!.Looks).IsEqualTo("Written by hand.");
    }

    [Test]
    public async Task UploadsAreCheckedLikeAnyOther()
    {
        using var w = await SeedAsync();

        var notAnImage = await w.Add().Handle(new AddBotReferenceCommand(w.Bot.Id, w.Owner.Id, "%PDF-1.7 not an image"u8.ToArray()), CancellationToken.None);
        var empty = await w.Add().Handle(new AddBotReferenceCommand(w.Bot.Id, w.Owner.Id, []), CancellationToken.None);
        var tooBig = await w.Add(new AttachmentOptions { MaxBytes = 1024 })
            .Handle(new AddBotReferenceCommand(w.Bot.Id, w.Owner.Id, TestImages.Face(Hair[0], 512, 512)), CancellationToken.None);

        await Assert.That(notAnImage.Error).IsEqualTo("Only PNG, JPEG, WebP and GIF images can be used.");
        await Assert.That(empty.Error).IsEqualTo("The file is empty.");
        await Assert.That(tooBig.Error!).StartsWith("Images can be at most");
        await Assert.That(w.Storage.BotReferences).IsEmpty();
        await Assert.That(w.Describer.Calls).IsEmpty();
    }

    [Test]
    public async Task SomeoneElse_CanNeitherAddRemoveNorSeeThem()
    {
        using var w = await SeedAsync();
        var mine = (await w.Add().Handle(new AddBotReferenceCommand(w.Bot.Id, w.Owner.Id, TestImages.Face(Hair[0])), CancellationToken.None)).Value!.References.Single();

        var add = await w.Add().Handle(new AddBotReferenceCommand(w.Bot.Id, w.Stranger.Id, TestImages.Face(Hair[1])), CancellationToken.None);
        var remove = await new RemoveBotReferenceHandler(w.Db, w.Storage).Handle(new RemoveBotReferenceCommand(w.Bot.Id, w.Stranger.Id, mine.Id), CancellationToken.None);
        var theirs = await new GetBotReferenceHandler(w.Db, w.Storage).Handle(new GetBotReferenceQuery(w.Bot.Id, mine.Id, w.Stranger.Id), CancellationToken.None);
        var own = await new GetBotReferenceHandler(w.Db, w.Storage).Handle(new GetBotReferenceQuery(w.Bot.Id, mine.Id, w.Owner.Id), CancellationToken.None);

        await Assert.That(add.IsFailure).IsTrue();
        await Assert.That(remove.IsFailure).IsTrue();
        await Assert.That(theirs.IsFailure).IsTrue();
        await Assert.That(own.IsSuccess).IsTrue();
        await Assert.That(own.Value!.ContentType).IsEqualTo("image/png");
        await Assert.That(w.Storage.BotReferences.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Removing_DeletesTheFile_AndKeepsTheLooks()
    {
        using var w = await SeedAsync();
        var reference = (await w.Add().Handle(new AddBotReferenceCommand(w.Bot.Id, w.Owner.Id, TestImages.Face(Hair[0])), CancellationToken.None)).Value!.References.Single();

        var removed = await new RemoveBotReferenceHandler(w.Db, w.Storage).Handle(new RemoveBotReferenceCommand(w.Bot.Id, w.Owner.Id, reference.Id), CancellationToken.None);

        await Assert.That(removed.IsSuccess).IsTrue();
        await Assert.That(w.Storage.BotReferences).IsEmpty();
        await Assert.That(await w.Db.BotReferences.CountAsync()).IsEqualTo(0);
        await Assert.That(w.Bot.Looks).IsNotNull();
    }

    [Test]
    public async Task DeletingTheBot_TakesItsReferencesAndLooks()
    {
        using var w = await SeedAsync();
        await w.Add().Handle(new AddBotReferenceCommand(w.Bot.Id, w.Owner.Id, TestImages.Face(Hair[0])), CancellationToken.None);

        await new DeleteBotHandler(w.Db, Substitute.For<IChatNotificationService>(), w.Storage).Handle(new DeleteBotCommand(w.Bot.Id, w.Owner.Id), CancellationToken.None);

        await Assert.That(w.Storage.BotReferences).IsEmpty();
        await Assert.That(await w.Db.BotReferences.CountAsync()).IsEqualTo(0);
        await Assert.That(w.Bot.Looks).IsNull();
    }

    [Test]
    public async Task TheCreator_EditsTheLooks_AndEmptyClearsThem()
    {
        using var w = await SeedAsync();
        var handler = new UpdateBotHandler(w.Db, Substitute.For<IOpenRouterService>(), new ModelPolicy(0, 0), Substitute.For<IChatBotState>());

        var edited = await handler.Handle(new UpdateBotCommand(w.Bot.Id, w.Owner.Id, null, null, null, null, null, null, null, Looks: "  Blonde bob, round glasses.  "), CancellationToken.None);
        await Assert.That(edited.IsSuccess).IsTrue();
        await Assert.That(w.Bot.Looks).IsEqualTo("Blonde bob, round glasses.");

        var stranger = await handler.Handle(new UpdateBotCommand(w.Bot.Id, w.Stranger.Id, null, null, null, null, null, null, null, Looks: "hacked"), CancellationToken.None);
        await Assert.That(stranger.IsFailure).IsTrue();
        await Assert.That(w.Bot.Looks).IsEqualTo("Blonde bob, round glasses.");

        await handler.Handle(new UpdateBotCommand(w.Bot.Id, w.Owner.Id, null, null, null, null, null, null, null, Looks: ""), CancellationToken.None);
        await Assert.That(w.Bot.Looks).IsNull();
    }

    [Test]
    public async Task LooksLongerThanTheLimit_AreRefused()
    {
        var validator = new UpdateBotValidator();
        var tooLong = new UpdateBotCommand(Guid.NewGuid(), Guid.NewGuid(), null, null, null, null, null, null, null, Looks: new string('a', Bot.MaxLooksLength + 1));
        var fits = tooLong with { Looks = new string('a', Bot.MaxLooksLength) };

        await Assert.That((await validator.ValidateAsync(tooLong)).IsValid).IsFalse();
        await Assert.That((await validator.ValidateAsync(fits)).IsValid).IsTrue();
    }

    [Test]
    public async Task ReferencesAndLooks_AreOnlyInTheCreatorsBotDto()
    {
        using var w = await SeedAsync();
        await w.Add().Handle(new AddBotReferenceCommand(w.Bot.Id, w.Owner.Id, TestImages.Face(Hair[0])), CancellationToken.None);
        var openRouter = Substitute.For<IOpenRouterService>();
        var list = new GetBotsHandler(w.Db, openRouter, new ModelPolicy(0, 0));
        var one = new GetBotHandler(w.Db, openRouter, new ModelPolicy(0, 0));

        var ownersList = (await list.Handle(new GetBotsQuery(null, w.Owner.Id), CancellationToken.None)).Value!.Single();
        var strangersList = (await list.Handle(new GetBotsQuery(null, w.Stranger.Id), CancellationToken.None)).Value!.Single();
        var anonymousList = (await list.Handle(new GetBotsQuery(), CancellationToken.None)).Value!.Single();
        var strangersOne = (await one.Handle(new GetBotQuery(w.Bot.Id, w.Stranger.Id), CancellationToken.None)).Value!;
        var ownersOne = (await one.Handle(new GetBotQuery(w.Bot.Id, w.Owner.Id), CancellationToken.None)).Value!;

        await Assert.That(ownersList.Looks).IsNotNull();
        await Assert.That(ownersList.References.Count).IsEqualTo(1);
        await Assert.That(ownersList.References[0].Url).StartsWith($"/api/bots/{w.Bot.Id}/references/");
        await Assert.That(ownersOne.References.Count).IsEqualTo(1);
        foreach (var dto in new[] { strangersList, anonymousList, strangersOne })
        {
            await Assert.That(dto.Looks).IsNull();
            await Assert.That(dto.References).IsEmpty();
        }
    }

    [Test]
    public async Task TheLooks_AreInTheBotsCharacter()
    {
        var bot = new Bot { Name = "Mia", ModelId = "m", SystemPrompt = "You are Mia, a barista.", CreatedById = Guid.NewGuid(), Looks = "Red hair in a messy bun." };

        var prompt = BotPromptBuilder.BuildSystemPrompt(bot, ["Alice"]);

        await Assert.That(BotPromptBuilder.Character(bot)).IsEqualTo("You are Mia, a barista.\n\nHow you look: Red hair in a messy bun.");
        await Assert.That(prompt).Contains("How you look: Red hair in a messy bun.");
        await Assert.That(BotPromptBuilder.Character(new Bot { Name = "X", ModelId = "m", SystemPrompt = "Plain.", CreatedById = Guid.NewGuid() })).IsEqualTo("Plain.");
    }
}
