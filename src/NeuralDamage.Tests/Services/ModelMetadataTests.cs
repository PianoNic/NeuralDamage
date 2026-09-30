using NeuralDamage.Application.Queries;
using NeuralDamage.Infrastructure.Services;
using NSubstitute;

namespace NeuralDamage.Tests.Services;

public class ModelMetadataTests
{
    [Test]
    [Arguments("deepseek/deepseek-chat-v3.1", "DeepSeek")]
    [Arguments("openai/gpt-5-mini", "OpenAI")]
    [Arguments("x-ai/grok-4-fast", "xAI")]
    [Arguments("meta-llama/llama-3.3-70b-instruct", "Meta")]
    [Arguments("mistralai/mistral-small", "Mistral")]
    [Arguments("qwen/qwen3-coder", "Qwen")]
    [Arguments("z-ai/glm-4.6", "Z.ai")]
    [Arguments("arcee-ai/virtuoso", "Arcee Ai")]
    [Arguments("noslash", "Noslash")]
    public async Task Provider_FromIdPrefix(string id, string expected)
    {
        await Assert.That(ModelMetadata.Provider(id)).IsEqualTo(expected);
    }

    [Test]
    public async Task Summary_IsTheFirstSentence_WithLinksFlattened()
    {
        var summary = ModelMetadata.Summary("  A fast model from [DeepSeek](https://deepseek.com).\nIt does v3.1 things. And more.");

        await Assert.That(summary).IsEqualTo("A fast model from DeepSeek.");
        await Assert.That(ModelMetadata.Summary("   ")).IsNull();
        await Assert.That(ModelMetadata.Summary("No full stop")).IsEqualTo("No full stop");
    }

    [Test]
    public async Task Capabilities_FromParametersModalitiesAndName()
    {
        var all = ModelMetadata.Capabilities("qwen/qwen3-coder-flash", "Qwen3 Coder Flash", ["tools", "include_reasoning"], ["text", "image"]);
        var none = ModelMetadata.Capabilities("meta-llama/llama-3.3-70b-instruct", "Llama 3.3 70B", ["tools"], ["text"]);

        await Assert.That(all).IsEquivalentTo([ModelMetadata.Reasoning, ModelMetadata.Vision, ModelMetadata.Code, ModelMetadata.Fast]);
        await Assert.That(none).IsEmpty();
    }

    [Test]
    public async Task ParseModels_ReadsMetadataFromTheCatalogue()
    {
        const string json = """
            {"data":[{
              "id":"openai/gpt-5-nano","name":"OpenAI: GPT-5 Nano","context_length":400000,
              "description":"The smallest GPT-5. Built for speed.",
              "pricing":{"prompt":"0.00000005","completion":"0.0000004"},
              "architecture":{"input_modalities":["text","image","file"]},
              "supported_parameters":["reasoning","tools"],
              "reasoning":{"mandatory":true,"supported_efforts":["low","medium","high"]}
            }]}
            """;

        var model = OpenRouterAgentService.ParseModels(json).Single();

        await Assert.That(model.Provider).IsEqualTo("OpenAI");
        await Assert.That(model.Description).IsEqualTo("The smallest GPT-5.");
        await Assert.That(model.Capabilities).IsEquivalentTo([ModelMetadata.Reasoning, ModelMetadata.Vision, ModelMetadata.Fast]);
        await Assert.That(model.Pricing).IsEqualTo(new ModelPricing(0.05m, 0.4m));
        await Assert.That(model.AcceptsReasoning).IsTrue();
        await Assert.That(model.ReasoningMandatory).IsTrue();
    }

    [Test]
    public async Task ParseModels_ReasoningFlagsDefaultToOff()
    {
        const string json = """
            {"data":[
              {"id":"a/optional","name":"Optional","supported_parameters":["include_reasoning"],"reasoning":{"mandatory":false}},
              {"id":"a/plain","name":"Plain"}
            ]}
            """;

        var models = OpenRouterAgentService.ParseModels(json);

        // include_reasoning alone shows the capability but does not take an effort.
        await Assert.That(models.Any(m => m.AcceptsReasoning || m.ReasoningMandatory)).IsFalse();
    }

    [Test]
    [Arguments(0.05, 0.10, 1)]
    [Arguments(0.10, 0.40, 2)]
    [Arguments(0.25, 0.60, 3)]
    public async Task PriceTier_ThirdsOfTheCap(double prompt, double completion, int tier)
    {
        var policy = new ModelPolicy(0.25m, 0.60m);

        await Assert.That(policy.PriceTier(new ModelPricing((decimal)prompt, (decimal)completion))).IsEqualTo(tier);
    }

    [Test]
    [Arguments(0.2, 0.6, 1)]
    [Arguments(1.0, 3.0, 2)]
    [Arguments(3.0, 15.0, 3)]
    public async Task PriceTier_NoCap_UsesFixedBands(double prompt, double completion, int tier)
    {
        await Assert.That(new ModelPolicy(0, 0).PriceTier(new ModelPricing((decimal)prompt, (decimal)completion))).IsEqualTo(tier);
    }

    [Test]
    public async Task PriceTier_UnknownPrice_IsTop()
    {
        await Assert.That(new ModelPolicy(0, 0).PriceTier(null)).IsEqualTo(3);
        await Assert.That(new ModelPolicy(0, 0).PriceTier(new ModelPricing(-1, -1))).IsEqualTo(3);
    }

    [Test]
    public async Task ListModels_SetsPriceTier()
    {
        var openRouter = Substitute.For<IOpenRouterService>();
        openRouter.ListModelsAsync(Arg.Any<CancellationToken>()).Returns([new OpenRouterModel("a/cheap", "Cheap", 8000, new ModelPricing(0.05m, 0.10m))]);
        var handler = new ListOpenRouterModelsHandler(openRouter, new ModelPolicy(0.25m, 0.60m));

        var model = (await handler.Handle(new ListOpenRouterModelsQuery(), CancellationToken.None)).Value!.Single();

        await Assert.That(model.PriceTier).IsEqualTo(1);
        await Assert.That(model.Provider).IsEqualTo("A");
    }
}
