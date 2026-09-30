using NeuralDamage.Application.Commands;
using NeuralDamage.Domain;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Tests.Helpers;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace NeuralDamage.Tests.Services;

/// <summary>Whether a bot's model still works, and what happens in chat when it does not.</summary>
public class ModelStatusTests
{
    private static IOpenRouterService Catalogue(params OpenRouterModel[] models)
    {
        var openRouter = Substitute.For<IOpenRouterService>();
        openRouter.ListModelsAsync(Arg.Any<CancellationToken>()).Returns(models.ToList());
        openRouter.ListZdrModelIdsAsync(Arg.Any<CancellationToken>()).Returns(new HashSet<string>(models.Select(m => m.Id)));
        return openRouter;
    }

    [Test]
    public async Task Lookup_AvailableMissingAndNotAllowed()
    {
        var openRouter = Catalogue(
            new OpenRouterModel("a/cheap", "Cheap", 8000, new ModelPricing(0.1m, 0.2m)),
            new OpenRouterModel("a/pricey", "Pricey", 8000, new ModelPricing(5m, 15m)));
        var lookup = await new ModelPolicy(0.25m, 0.60m).StatusLookupAsync(openRouter);

        await Assert.That(lookup("a/cheap")).IsEqualTo(ModelStatus.Ok);

        var missing = lookup("a/retired");
        await Assert.That(missing.Status).IsEqualTo(ModelStatus.Missing);
        await Assert.That(missing.Reason!).Contains("a/retired");

        var refused = lookup("a/pricey");
        await Assert.That(refused.Status).IsEqualTo(ModelStatus.NotAllowed);
        await Assert.That(refused.Reason!).Contains("price cap");
    }

    [Test]
    public async Task Lookup_CatalogueUnreachable_TreatsEverythingAsAvailable()
    {
        var openRouter = Substitute.For<IOpenRouterService>();
        openRouter.ListModelsAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new HttpRequestException("down"));

        var lookup = await new ModelPolicy(0.25m, 0.60m).StatusLookupAsync(openRouter);

        await Assert.That(lookup("anything").IsAvailable).IsTrue();
    }

    [Test]
    public async Task Orchestrator_SkipsBrokenModel_AndSaysSoOnce()
    {
        using var h = await OrchestratorHarness.CreateAsync(botCount: 2);
        var (gpt, claude) = (h.Bots[0], h.Bots[1]);
        // Only Claude's model is still in the catalogue.
        h.OpenRouter.ListModelsAsync(Arg.Any<CancellationToken>()).Returns([new OpenRouterModel(claude.ModelId, "Claude", 8000, new ModelPricing(0.1m, 0.2m))]);
        h.Respond(gpt, claude);
        h.Reply("hi", "hello again");

        await h.Orchestrator.ProcessMessageAsync(h.Chat.Id, (await h.SayAsync("hey all")).Id);
        await h.Orchestrator.ProcessMessageAsync(h.Chat.Id, (await h.SayAsync("anyone?")).Id);

        await Assert.That(await h.MessagesFromAsync(gpt)).IsEmpty();
        await Assert.That((await h.MessagesFromAsync(claude)).Count).IsEqualTo(2);
        await h.OpenRouter.DidNotReceive().GenerateResponseAsync(gpt.ModelId, Arg.Any<double>(), Arg.Any<string>(), Arg.Any<List<ChatMessage>>(), Arg.Any<CancellationToken>());
        await h.Notifications.Received(1).NotifySystemMessage(h.Chat.Id, $"GPT's model {gpt.ModelId} is no longer available. Edit the bot to pick another.");
    }

    [Test]
    public async Task ChangingTheModel_LetsTheNoticeShowAgain()
    {
        using var db = TestDbContext.Create();
        var user = new User { ExternalId = "ext-1", Email = "a@test.com" };
        var bot = new Bot { Name = "GPT", ModelId = "a/old", SystemPrompt = "x", CreatedById = user.Id };
        db.AddRange(user, bot);
        await db.SaveChangesAsync();
        var state = new ChatBotState();
        var chatId = Guid.NewGuid();
        await Assert.That(state.TryMarkModelNotice(chatId, bot.Id)).IsTrue();
        await Assert.That(state.TryMarkModelNotice(chatId, bot.Id)).IsFalse();

        var handler = new UpdateBotHandler(db, Substitute.For<IOpenRouterService>(), new ModelPolicy(0, 0), state);
        var result = await handler.Handle(new UpdateBotCommand(bot.Id, user.Id, null, "a/new", null, null, null, null, null), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(state.TryMarkModelNotice(chatId, bot.Id)).IsTrue();
    }
}
