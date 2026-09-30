using Microsoft.Extensions.Configuration;
using NeuralDamage.Infrastructure.Services;

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

        await Assert.That(policy).IsEqualTo(new ModelPolicy(0.25m, 0.60m, ZdrOnly: true, ExcludeBatchModels: true));
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
        var policy = FromConfig(("OpenRouter:ZdrOnly", "false"), ("OpenRouter:ExcludeBatchModels", "false"), ("OpenRouter:MaxPromptPrice", "1.5"));

        await Assert.That(policy).IsEqualTo(new ModelPolicy(1.5m, 0.60m, ZdrOnly: false, ExcludeBatchModels: false));
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
}
