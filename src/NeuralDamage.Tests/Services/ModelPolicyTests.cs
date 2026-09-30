using Microsoft.Extensions.Configuration;
using NeuralDamage.Infrastructure.Services;
using NSubstitute;

namespace NeuralDamage.Tests.Services;

public class ModelPolicyTests
{
    private static ModelPolicy FromConfig(params (string Key, string Value)[] values) =>
        ModelPolicy.FromConfiguration(new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value))
            .Build());

    [Test]
    public async Task Unset_DefaultsToSafeAndCheap()
    {
        var policy = FromConfig();

        await Assert.That(policy).IsEqualTo(new ModelPolicy(0.25m, 0.60m, ZdrOnly: true, ExcludeBatchModels: true, DisableReasoning: true));
    }

    [Test]
    public async Task Blank_FallsBackToDefaultPrice()
    {
        var policy = FromConfig(("OpenRouter:MaxPromptPrice", ""), ("OpenRouter:MaxCompletionPrice", " "));

        await Assert.That(policy.MaxPromptPrice).IsEqualTo(0.25m);
        await Assert.That(policy.MaxCompletionPrice).IsEqualTo(0.60m);
    }

    [Test]
    public async Task ExplicitZero_MeansNoLimit()
    {
        var policy = FromConfig(("OpenRouter:MaxPromptPrice", "0"), ("OpenRouter:MaxCompletionPrice", "0"));

        await Assert.That(policy.IsPriceUnlimited).IsTrue();
        await Assert.That(policy.Refusal("pricey/model", new ModelPricing(15m, 75m), new HashSet<string> { "pricey/model" })).IsNull();
    }

    [Test]
    public async Task Switches_CanBeTurnedOff()
    {
        var policy = FromConfig(("OpenRouter:ZdrOnly", "false"), ("OpenRouter:ExcludeBatchModels", "false"), ("OpenRouter:MaxPromptPrice", "1.5"), ("OpenRouter:DisableReasoning", "false"));

        await Assert.That(policy).IsEqualTo(new ModelPolicy(1.5m, 0.60m, ZdrOnly: false, ExcludeBatchModels: false, DisableReasoning: false));
        await Assert.That(policy.Refusal("cheap/model:batch", new ModelPricing(0.1m, 0.2m), new HashSet<string>())).IsNull();
    }

    [Test]
    public async Task Refusal_NamesTheReason()
    {
        var policy = new ModelPolicy(0.25m, 0.60m, ZdrOnly: true, ExcludeBatchModels: true);
        var zdr = new HashSet<string> { "cheap/model", "pricey/model" };

        await Assert.That(policy.Refusal("cheap/model", new ModelPricing(0.1m, 0.4m), zdr)).IsNull();
        await Assert.That(policy.Refusal("cheap/model:batch", new ModelPricing(0.1m, 0.4m), zdr)!).Contains("batch");
        await Assert.That(policy.Refusal("retaining/model", new ModelPricing(0.1m, 0.4m), zdr)!).Contains("zero-data-retention");
        await Assert.That(policy.Refusal("pricey/model", new ModelPricing(2m, 8m), zdr)!).Contains("price cap");
    }

    private static readonly OpenRouterModel AlwaysReasons = new("a/always-reasons", "Always reasons", 8000, new ModelPricing(0.1m, 0.2m)) { AcceptsReasoning = true, ReasoningMandatory = true };
    private static readonly OpenRouterModel CanReason = new("a/can-reason", "Can reason", 8000, new ModelPricing(0.1m, 0.2m)) { AcceptsReasoning = true };

    private static IOpenRouterService Catalogue()
    {
        var openRouter = Substitute.For<IOpenRouterService>();
        openRouter.ListModelsAsync(Arg.Any<CancellationToken>()).Returns([AlwaysReasons, CanReason]);
        return openRouter;
    }

    [Test]
    public async Task ReasoningOff_RefusesModelsThatAlwaysReason()
    {
        var policy = new ModelPolicy(0.25m, 0.60m, DisableReasoning: true);
        var openRouter = Catalogue();

        // Hidden from the model browser...
        var allowed = await policy.FilterAsync(openRouter, await openRouter.ListModelsAsync());
        await Assert.That(allowed.Select(m => m.Id)).IsEquivalentTo(["a/can-reason"]);

        // ...refused on create or switch...
        await Assert.That((await policy.CheckModelAsync(openRouter, AlwaysReasons.Id))!).Contains("always reasons");
        await Assert.That(await policy.CheckModelAsync(openRouter, CanReason.Id)).IsNull();

        // ...and flagged on bots that already use one.
        var status = (await policy.StatusLookupAsync(openRouter))(AlwaysReasons.Id);
        await Assert.That(status.Status).IsEqualTo(ModelStatus.NotAllowed);
        await Assert.That(status.Reason!).Contains("OpenRouter:DisableReasoning");
    }

    [Test]
    public async Task ReasoningOff_ChecksTheCatalogueEvenWithoutAPriceCap()
    {
        var policy = new ModelPolicy(0, 0, DisableReasoning: true);

        await Assert.That(await policy.CheckModelAsync(Catalogue(), AlwaysReasons.Id)).IsNotNull();
    }

    [Test]
    public async Task ReasoningOn_AllowsModelsThatAlwaysReason()
    {
        var policy = new ModelPolicy(0.25m, 0.60m, DisableReasoning: false);
        var openRouter = Catalogue();

        await Assert.That((await policy.FilterAsync(openRouter, await openRouter.ListModelsAsync())).Count).IsEqualTo(2);
        await Assert.That(await policy.CheckModelAsync(openRouter, AlwaysReasons.Id)).IsNull();
        await Assert.That((await policy.StatusLookupAsync(openRouter))(AlwaysReasons.Id)).IsEqualTo(ModelStatus.Ok);
    }
}
