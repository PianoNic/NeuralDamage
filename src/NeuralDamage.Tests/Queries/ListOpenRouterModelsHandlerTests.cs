using NeuralDamage.Application.Queries;
using NeuralDamage.Infrastructure.Services;
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
        ]);
        return openRouter;
    }

    [Test]
    public async Task Handle_WithCap_ReturnsOnlyModelsWithinIt()
    {
        var handler = new ListOpenRouterModelsHandler(Catalogue(), new ModelPriceCap(0.25m, 0.60m));
        var result = await handler.Handle(new ListOpenRouterModelsQuery(), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Value!.Select(m => m.Id)).IsEquivalentTo(["cheap/model"]);
        await Assert.That(result.Value![0].Pricing).IsEqualTo(new ModelPricing(0.10m, 0.40m));
    }

    [Test]
    public async Task Handle_OneSidedCap_IgnoresTheOtherSide()
    {
        var handler = new ListOpenRouterModelsHandler(Catalogue(), new ModelPriceCap(0.25m, 0));
        var result = await handler.Handle(new ListOpenRouterModelsQuery(), CancellationToken.None);

        await Assert.That(result.Value!.Select(m => m.Id)).IsEquivalentTo(["cheap/model", "pricey-completion/model"]);
    }

    [Test]
    public async Task Handle_NoCap_ReturnsEverything()
    {
        var handler = new ListOpenRouterModelsHandler(Catalogue(), new ModelPriceCap(0, 0));
        var result = await handler.Handle(new ListOpenRouterModelsQuery(), CancellationToken.None);

        await Assert.That(result.Value!.Count).IsEqualTo(5);
    }
}
