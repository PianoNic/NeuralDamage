using Microsoft.Extensions.Configuration;
using NeuralDamage.Application.Queries;
using NeuralDamage.Infrastructure.Services;
using NSubstitute;

namespace NeuralDamage.Tests.Queries;

/// <summary>The picker's Recommended list, from <c>Recommendations:N:Model</c> and <c>:Hint</c>.</summary>
public class ModelRecommendationTests
{
    private static IConfiguration Config(Dictionary<string, string?> settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

    [Test]
    public async Task FromConfiguration_ReadsEntriesInOrder_SkippingOnesWithoutAModel()
    {
        var recommendations = ModelRecommendation.FromConfiguration(Config(new()
        {
            ["Recommendations:10:Model"] = "late/model",
            ["Recommendations:10:Hint"] = "Last",
            ["Recommendations:0:Model"] = " first/model ",
            ["Recommendations:0:Hint"] = " Best all-rounder ",
            ["Recommendations:2:Hint"] = "No model, so skipped",
            ["Recommendations:3:Model"] = "no/hint",
        }));

        await Assert.That(recommendations).IsEquivalentTo(
            [
                new ModelRecommendation("first/model", "Best all-rounder"),
                new ModelRecommendation("no/hint", ""),
                new ModelRecommendation("late/model", "Last"),
            ],
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task FromConfiguration_ShippedDefaults_PutTheGatewayDeepSeekFirst()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(directory.FullName, "src", "NeuralDamage.API", "appsettings.json")))
            directory = directory.Parent ?? throw new FileNotFoundException("The API's appsettings.json is not above the test binaries.");
        var path = Path.Combine(directory.FullName, "src", "NeuralDamage.API", "appsettings.json");
        var recommendations = ModelRecommendation.FromConfiguration(new ConfigurationBuilder().AddJsonFile(path).Build());

        await Assert.That(recommendations.Count).IsEqualTo(6);
        await Assert.That(recommendations[0].Model).IsEqualTo("gateway:sgl/deepseek");
        await Assert.That(recommendations.All(r => r.Hint.Length > 0)).IsTrue();
    }

    [Test]
    public async Task Handle_RanksRecommendedModels_DefaultFirst_AndLeavesOutOnesNotOffered()
    {
        var openRouter = Substitute.For<IOpenRouterService>();
        openRouter.ListModelsAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new OpenRouterModel("cheap/a", "A", 8000, new ModelPricing(0.10m, 0.40m)),
            new OpenRouterModel("cheap/b", "B", 8000, new ModelPricing(0.10m, 0.40m)),
            new OpenRouterModel("pricey/c", "C", 8000, new ModelPricing(5m, 15m)),
            new OpenRouterModel("cheap/d", "D", 8000, new ModelPricing(0.10m, 0.40m)),
        ]);
        var configuration = Config(new()
        {
            ["App:DefaultModel"] = "cheap/b",
            ["Recommendations:0:Model"] = "cheap/a",
            ["Recommendations:0:Hint"] = "For anything",
            ["Recommendations:1:Model"] = "gone/model",
            ["Recommendations:1:Hint"] = "Not offered any more",
            ["Recommendations:2:Model"] = "pricey/c",
            ["Recommendations:2:Hint"] = "Over the price cap",
            ["Recommendations:3:Model"] = "cheap/b",
            ["Recommendations:3:Hint"] = "The default",
        });
        var handler = new ListOpenRouterModelsHandler(openRouter, new ModelPolicy(0.25m, 0.60m), configuration);

        var models = (await handler.Handle(new ListOpenRouterModelsQuery(), CancellationToken.None)).Value!;

        var recommended = models.Where(m => m.RecommendationRank is not null).OrderBy(m => m.RecommendationRank).ToList();
        await Assert.That(recommended.Select(m => m.Id)).IsEquivalentTo(["cheap/b", "cheap/a"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(recommended.Select(m => m.RecommendationRank ?? -1)).IsEquivalentTo([0, 1], TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(recommended[0].IsDefault).IsTrue();
        await Assert.That(recommended[0].RecommendationHint).IsEqualTo("The default");
        await Assert.That(recommended[1].RecommendationHint).IsEqualTo("For anything");
        await Assert.That(models.Single(m => m.Id == "cheap/d").RecommendationRank).IsNull();
        await Assert.That(models.Select(m => m.Id)).DoesNotContain("pricey/c");
    }

    [Test]
    public async Task Apply_DefaultNotInTheList_StillComesFirst_WithoutAHint()
    {
        var models = ModelRecommendation.Apply(
            [new ModelRecommendation("x/one", "One")],
            [new OpenRouterModel("x/one", "One", null), new OpenRouterModel("gateway:m", "gateway: m", null)],
            "gateway:m");

        await Assert.That(models.Single(m => m.Id == "gateway:m").RecommendationRank).IsEqualTo(0);
        await Assert.That(models.Single(m => m.Id == "gateway:m").RecommendationHint).IsNull();
        await Assert.That(models.Single(m => m.Id == "x/one").RecommendationRank).IsEqualTo(1);
    }
}
