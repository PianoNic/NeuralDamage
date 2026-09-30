using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NeuralDamage.Domain;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.Attachments;
using NeuralDamage.Infrastructure.Services.BotDecision;
using NeuralDamage.Tests.Helpers;
using NSubstitute;

namespace NeuralDamage.Tests.Infrastructure;

/// <summary>How a picture reaches each bot: as the image itself, or as its description.</summary>
public class ImagePromptTests
{
    private static Message Said(string content, User alice, params Attachment[] attachments)
    {
        var message = new Message { ChatId = Guid.NewGuid(), SenderUserId = alice.Id, SenderUser = alice, Content = content };
        foreach (var a in attachments)
            message.Attachments.Add(a);
        return message;
    }

    private static Attachment Picture(User alice, string? description) =>
        new() { ChatId = Guid.NewGuid(), UploaderUserId = alice.Id, ContentType = "image/png", SizeBytes = 64, Description = description };

    [Test]
    public async Task TextOnlyHistory_WritesTheDescriptionIn()
    {
        var alice = new User { ExternalId = "a", Email = "a@test.com", DisplayName = "alice" };
        var message = Said("dinner", alice, Picture(alice, "A margherita pizza with fresh basil."));

        var history = BotPromptBuilder.BuildHistory([message], Guid.NewGuid());

        await Assert.That(history.Single().Content).IsEqualTo("[alice] (you're answering this): dinner\n[image from alice: A margherita pizza with fresh basil.]");
        await Assert.That(history.Single().Images).IsEmpty();
    }

    [Test]
    public async Task WithoutADescription_TheImageIsStillMentioned()
    {
        var alice = new User { ExternalId = "a", Email = "a@test.com", DisplayName = "alice" };
        var message = Said("", alice, Picture(alice, null));

        var history = BotPromptBuilder.BuildHistory([message], Guid.NewGuid());

        await Assert.That(history.Single().Content).EndsWith(": [image from alice]");
    }

    [Test]
    public async Task VisionHistory_SendsThePicture_InsteadOfTheDescription()
    {
        var alice = new User { ExternalId = "a", Email = "a@test.com", DisplayName = "alice" };
        var picture = Picture(alice, "A margherita pizza.");
        var message = Said("dinner", alice, picture);
        var image = new ImagePart("image/png", TestImages.Png());

        var history = BotPromptBuilder.BuildHistory([message], Guid.NewGuid(), images: new Dictionary<Guid, ImagePart> { [picture.Id] = image });

        await Assert.That(history.Single().Content).EndsWith("dinner\n[image from alice]");
        await Assert.That(history.Single().Images).IsEquivalentTo([image]);
    }

    [Test]
    public async Task ImagesGoOnTheWire_AsContentParts()
    {
        List<ChatMessage> history = [new("user", "[alice]: look") { Images = [new ImagePart("image/png", [1, 2, 3])] }, BotPromptBuilder.BuildNote()];

        var json = OpenRouterAgentService.MessagesWithCacheBreakpoint("system", history, 0).ToString();

        await Assert.That(json).Contains("""{"role":"user","content":[{"type":"text","text":"[alice]: look"},{"type":"image_url","image_url":{"url":"data:image/png;base64,AQID"},"cache_control":{"type":"ephemeral"}}]}""");
    }

    [Test]
    public async Task InOneChat_TheVisionBotSeesThePicture_AndTheTextOnlyBotReadsTheDescription()
    {
        var storage = new InMemoryAttachmentStorage();
        using var h = await OrchestratorHarness.CreateAsync(botCount: 2, configure: s => s.AddSingleton<IAttachmentStorage>(storage));
        var (gpt, claude) = (h.Bots[0], h.Bots[1]);
        h.OpenRouter.ListModelsAsync(Arg.Any<CancellationToken>()).Returns([
            new OpenRouterModel(gpt.ModelId, "GPT", 1000) { Capabilities = [ModelMetadata.Vision] },
            new OpenRouterModel(claude.ModelId, "Claude", 1000) { Capabilities = [] },
        ]);
        h.Respond(gpt, claude);
        h.Reply("that looks great", "pizza night!");

        var trigger = await h.SayAsync("made dinner");
        var picture = new Attachment { ChatId = h.Chat.Id, UploaderUserId = h.User.Id, MessageId = trigger.Id, ContentType = "image/png", SizeBytes = 64, Description = "A margherita pizza with fresh basil." };
        h.Db.Attachments.Add(picture);
        await h.Db.SaveChangesAsync();
        await storage.SaveAsync(h.Chat.Id, picture.Id, TestImages.Png());

        await h.Orchestrator.ProcessMessageAsync(h.Chat.Id, trigger.Id);

        await h.OpenRouter.Received(1).GenerateResponseAsync(gpt.ModelId, Arg.Any<double>(), Arg.Any<string>(),
            Arg.Is<List<ChatMessage>>(history => history.Any(m => m.Images.Count == 1 && m.Content.EndsWith("[image from Alice]"))
                && !history.Any(m => m.Content.Contains("margherita"))),
            Arg.Any<CancellationToken>());
        await h.OpenRouter.Received(1).GenerateResponseAsync(claude.ModelId, Arg.Any<double>(), Arg.Any<string>(),
            Arg.Is<List<ChatMessage>>(history => history.All(m => m.Images.Count == 0)
                && history.Any(m => m.Content.Contains("[image from Alice: A margherita pizza with fresh basil.]"))),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TheRound_WaitsForTheDescriber_BeforeAnyoneAnswers()
    {
        var describer = Substitute.For<IImageDescriber>();
        using var h = await OrchestratorHarness.CreateAsync(configure: s =>
        {
            s.AddSingleton(describer);
            s.AddSingleton(new AttachmentOptions { DescriptionWait = TimeSpan.FromSeconds(3) });
        });
        h.Respond(h.Bots[0]);
        h.Reply("nice");
        var trigger = await h.SayAsync("");
        var picture = new Attachment { ChatId = h.Chat.Id, UploaderUserId = h.User.Id, MessageId = trigger.Id, ContentType = "image/png", SizeBytes = 64 };
        h.Db.Attachments.Add(picture);
        await h.Db.SaveChangesAsync();

        await h.Orchestrator.ProcessMessageAsync(h.Chat.Id, trigger.Id);

        await describer.Received(1).WaitAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Single() == picture.Id), TimeSpan.FromSeconds(3), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Jev_ReadsTheDescription()
    {
        var alice = new User { ExternalId = "a", Email = "a@test.com", DisplayName = "alice" };
        var message = Said("", alice, Picture(alice, "A steaming bowl of ramen."));
        object? state = null;
        var decisions = Substitute.For<IDecisionsClient>();
        decisions.DecideAsync(Arg.Do<object>(s => state = s), Arg.Any<IReadOnlyDictionary<string, DecisionQuestion>>(), Arg.Any<CancellationToken>())
            .Returns((DecisionsResponse?)null);
        var cook = new Bot { Name = "Cook", ModelId = "m", SystemPrompt = "a chef", CreatedById = alice.Id };

        using var db = TestDbContext.Create();
        db.Messages.Add(message);
        await db.SaveChangesAsync();

        await new BotDecisionEngine(db, decisions, new BotRankingOptions(), NullLogger<BotDecisionEngine>.Instance)
            .DecideAsync(message.ChatId, message, [cook], CancellationToken.None);

        await Assert.That(JsonSerializer.Serialize(state)).Contains("[image from alice: A steaming bowl of ramen.]");
    }
}
