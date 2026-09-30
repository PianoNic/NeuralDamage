using NeuralDamage.Application.Queries;
using NeuralDamage.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using NSubstitute;

namespace NeuralDamage.Tests.Queries;

public class ListOpenRouterModelsHandlerTests
{
    private static IOpenRouterService Catalogue()
    {
        var openRouter = Substitute.For<IOpenRouterService>();
        openRouter.ListModelsAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new OpenRouterModel("cheap/model", "Cheap", 8000, new ModelPricing(0.10m, 0.40m)),
            new OpenRouterModel("pricey-prompt/model", "Pricey prompt", 8000, new ModelPricing(0.30m, 0.40m)),
            new OpenRouterModel("pricey-completion/model", "Pricey completion", 8000, new ModelPricing(0.10m, 0.70m)),
            new OpenRouterModel("openrouter/auto", "Auto", 8000, new ModelPricing(-1m, -1m)),
            new OpenRouterModel("no/pricing", "No pricing", 8000),
            new OpenRouterModel("cheap/retaining", "Cheap, retains prompts", 8000, new ModelPricing(0.05m, 0.10m)),
            new OpenRouterModel("cheap/model:batch", "Cheap (batch)", 8000, new ModelPricing(0.05m, 0.20m)),
        ]);
        // Everything but cheap/retaining has a ZDR endpoint; the batch id is
        // listed so the batch rule is what removes it.
        openRouter.ListZdrModelIdsAsync(Arg.Any<CancellationToken>()).Returns(new HashSet<string>
        {
            "cheap/model", "pricey-prompt/model", "pricey-completion/model", "openrouter/auto", "no/pricing", "cheap/model:batch",
        });
        return openRouter;
    }

    [Test]
    public async Task Handle_WithCap_ReturnsOnlyModelsWithinIt()
    {
        var handler = new ListOpenRouterModelsHandler(Catalogue(), new ModelPolicy(0.25m, 0.60m));
        var result = await handler.Handle(new ListOpenRouterModelsQuery(), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Value!.Select(m => m.Id)).IsEquivalentTo(["cheap/model", "cheap/retaining", "cheap/model:batch"]);
        await Assert.That(result.Value![0].Pricing).IsEqualTo(new ModelPricing(0.10m, 0.40m));
    }

    [Test]
    public async Task Handle_OneSidedCap_IgnoresTheOtherSide()
    {
        var handler = new ListOpenRouterModelsHandler(Catalogue(), new ModelPolicy(0.25m, 0));
        var result = await handler.Handle(new ListOpenRouterModelsQuery(), CancellationToken.None);

        await Assert.That(result.Value!.Select(m => m.Id)).IsEquivalentTo(["cheap/model", "pricey-completion/model", "cheap/retaining", "cheap/model:batch"]);
    }

    [Test]
    public async Task Handle_NoCap_ReturnsEverything()
    {
        var handler = new ListOpenRouterModelsHandler(Catalogue(), new ModelPolicy(0, 0));
        var result = await handler.Handle(new ListOpenRouterModelsQuery(), CancellationToken.None);

        await Assert.That(result.Value!.Count).IsEqualTo(7);
    }

    [Test]
    public async Task Handle_ZdrOnly_DropsModelsWithoutZdrEndpoint()
    {
        var handler = new ListOpenRouterModelsHandler(Catalogue(), new ModelPolicy(0, 0, ZdrOnly: true));
        var result = await handler.Handle(new ListOpenRouterModelsQuery(), CancellationToken.None);

        await Assert.That(result.Value!.Select(m => m.Id)).DoesNotContain("cheap/retaining");
        await Assert.That(result.Value!.Count).IsEqualTo(6);
    }

    [Test]
    public async Task Handle_ExcludeBatch_DropsBatchVariants()
    {
        var handler = new ListOpenRouterModelsHandler(Catalogue(), new ModelPolicy(0, 0, ExcludeBatchModels: true));
        var result = await handler.Handle(new ListOpenRouterModelsQuery(), CancellationToken.None);

        await Assert.That(result.Value!.Select(m => m.Id)).DoesNotContain("cheap/model:batch");
        await Assert.That(result.Value!.Count).IsEqualTo(6);
    }

    [Test]
    public async Task Handle_SafeDefaults_ReturnOnlyCheapZdrNonBatch()
    {
        var policy = ModelPolicy.FromConfiguration(new ConfigurationBuilder().Build());
        var handler = new ListOpenRouterModelsHandler(Catalogue(), policy);
        var result = await handler.Handle(new ListOpenRouterModelsQuery(), CancellationToken.None);

        await Assert.That(result.Value!.Select(m => m.Id)).IsEquivalentTo(["cheap/model"]);
    }

    [Test]
    public async Task Handle_PolicyOff_DoesNotFetchZdrList()
    {
        var openRouter = Catalogue();
        var handler = new ListOpenRouterModelsHandler(openRouter, new ModelPolicy(0, 0));
        await handler.Handle(new ListOpenRouterModelsQuery(), CancellationToken.None);

        await openRouter.DidNotReceive().ListZdrModelIdsAsync(Arg.Any<CancellationToken>());
    }
}
