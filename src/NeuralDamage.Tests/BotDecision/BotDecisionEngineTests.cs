using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NeuralDamage.Domain;
using NeuralDamage.Infrastructure.Dtos;
using NeuralDamage.Infrastructure.Services.BotDecision;
using NeuralDamage.Tests.Helpers;
using NSubstitute;
using static NeuralDamage.Infrastructure.Services.BotDecision.BotDecisionEngine;

namespace NeuralDamage.Tests.BotDecision;

/// <summary>Jev decides every bot, once per message; the code only maps its answers.</summary>
public class BotDecisionEngineTests
{
    private static BotDecisionEngine Engine(OrchestratorHarness h, FakeJev jev, BotRankingOptions? ranking = null) =>
        new(h.Db, jev, ranking ?? new BotRankingOptions(), NullLogger<BotDecisionEngine>.Instance);

    private static async Task<List<BotVerdict>> DecideAsync(OrchestratorHarness h, FakeJev jev, Message message, BotRankingOptions? ranking = null, List<Bot>? bots = null)
    {
        var loaded = await h.Db.Messages.AsNoTracking()
            .Include(m => m.SenderUser).Include(m => m.SenderBot).Include(m => m.Attachments)
            .Include(m => m.ReplyTo!).ThenInclude(r => r.SenderBot)
            .Include(m => m.ReplyTo!).ThenInclude(r => r.SenderUser)
            .FirstAsync(m => m.Id == message.Id);
        return await Engine(h, jev, ranking).DecideAsync(h.Chat.Id, loaded, bots ?? h.Bots);
    }

    [Test]
    [Arguments(1)]
    [Arguments(4)]
    public async Task EveryMessage_IsOneDecisionsCall_CoveringEveryBot(int botCount)
    {
        var jev = new FakeJev(_ => FakeJev.Chose(Quiet, 0.9));
        using var h = await OrchestratorHarness.CreateAsync(botCount: botCount, configure: jev.Engine());
        var message = await h.SayAsync("anyone seen the new dune movie?");

        await h.Orchestrator.ProcessMessageAsync(h.Chat.Id, message.Id);

        await Assert.That(jev.Calls).HasSingleItem();
        await Assert.That(jev.Calls[0].Questions.Keys).IsEquivalentTo(Enumerable.Range(0, botCount).Select(i => $"bot_{i}").Append(HealthKey));
        await Assert.That(jev.Calls[0].State.Bots.Values.Select(b => b.Name)).IsEquivalentTo(h.Bots.Select(b => b.Name));
    }

    [Test]
    public async Task MutedBots_AndTheSender_AreLeftOutOfTheQuestion()
    {
        var jev = new FakeJev(_ => FakeJev.Chose(Quiet, 0.9));
        using var h = await OrchestratorHarness.CreateAsync(botCount: 4, configure: jev.Engine());
        h.BotState.Mute(h.Chat.Id, h.Bots[1].Id);
        var message = await h.SayAsync("i'm back", asBot: h.Bots[3]);

        await h.Orchestrator.ProcessMessageAsync(h.Chat.Id, message.Id);

        await Assert.That(jev.Calls).HasSingleItem();
        await Assert.That(jev.Calls[0].Questions.Count).IsEqualTo(3); // two bots and the health
        await Assert.That(jev.Calls[0].State.Bots.Values.Select(b => b.Name)).IsEquivalentTo([h.Bots[0].Name, h.Bots[2].Name]);
    }

    [Test]
    public async Task StoppedChat_AsksNobody()
    {
        var jev = new FakeJev(_ => FakeJev.Chose(Reply, 0.9));
        using var h = await OrchestratorHarness.CreateAsync(botCount: 2, configure: jev.Engine());
        h.BotState.Stop(h.Chat.Id);
        var message = await h.SayAsync("anyone?");

        await h.Orchestrator.ProcessMessageAsync(h.Chat.Id, message.Id);

        await Assert.That(jev.Calls).IsEmpty();
        await h.Notifications.DidNotReceiveWithAnyArgs().NotifyMessageNew(default, default!);
    }

    [Test]
    public async Task Request_HasTheAgreedShape()
    {
        var jev = new FakeJev(_ => FakeJev.Chose(Quiet, 0.9));
        using var h = await OrchestratorHarness.CreateAsync(botCount: 2);
        h.Bots[0].Personality = "Cynical film critic.";
        h.Bots[0].SystemPrompt = "Hates blockbusters, loves arguing.";
        await h.Db.SaveChangesAsync();
        await h.SayAsync("i'm making gyoza", asBot: h.Bots[1], at: DateTime.UtcNow.AddMinutes(-1));
        var message = await h.SayAsync("anyone seen the new dune movie?");

        await DecideAsync(h, jev, message);

        var (state, questions) = jev.Calls.Single();
        using var body = JsonDocument.Parse(JsonSerializer.Serialize(
            new DecisionsRequest(BotRankingOptions.DefaultModel, state, questions), DecisionsClient.JsonOptions));
        var root = body.RootElement;
        await Assert.That(root.GetProperty("model").GetString()).IsEqualTo("~typesafe/jev-latest");

        var s = root.GetProperty("state");
        await Assert.That(s.EnumerateObject().Select(p => p.Name)).IsEquivalentTo(["chat", "recent_messages", "new_message", "flow", "bots"]);
        // A person just wrote: nothing has piled up behind them.
        var flow = s.GetProperty("flow");
        await Assert.That(flow.GetProperty("bot_messages_since_last_human").GetInt32()).IsEqualTo(0);
        await Assert.That(flow.GetProperty("seconds_since_last_human").GetInt32()).IsLessThanOrEqualTo(1);
        await Assert.That(flow.GetProperty("humans_active").GetBoolean()).IsTrue();
        await Assert.That(s.GetProperty("chat").GetString()).IsEqualTo("Chat");
        var recent = s.GetProperty("recent_messages");
        await Assert.That(recent.GetArrayLength()).IsEqualTo(1); // the new message is not repeated as history
        await Assert.That(recent[0].GetProperty("sender").GetString()).IsEqualTo("Claude");
        await Assert.That(recent[0].GetProperty("is_bot").GetBoolean()).IsTrue();
        await Assert.That(recent[0].GetProperty("text").GetString()).IsEqualTo("i'm making gyoza");
        var newMessage = s.GetProperty("new_message");
        await Assert.That(newMessage.EnumerateObject().Select(p => p.Name)).IsEquivalentTo(["sender", "is_bot", "text"]);
        await Assert.That(newMessage.GetProperty("sender").GetString()).IsEqualTo("Alice");
        await Assert.That(newMessage.GetProperty("is_bot").GetBoolean()).IsFalse();
        await Assert.That(s.GetProperty("bots").GetProperty("bot_0").GetProperty("name").GetString()).IsEqualTo("GPT");
        await Assert.That(s.GetProperty("bots").GetProperty("bot_0").GetProperty("persona").GetString())
            .IsEqualTo("Cynical film critic. Hates blockbusters, loves arguing.");

        foreach (var key in new[] { "bot_0", "bot_1" })
        {
            var q = root.GetProperty("questions").GetProperty(key);
            await Assert.That(q.GetProperty("type").GetString()).IsEqualTo("choice");
            await Assert.That(q.GetProperty("instructions").GetString()).IsEqualTo(
                $"What would the person in `bots.{key}`, going only by their persona, naturally do with `new_message` in this group chat?");
            var criteria = q.GetProperty("criteria");
            await Assert.That(criteria.EnumerateObject().Select(p => p.Name))
                .IsEquivalentTo(["reply", "react_laugh", "react_love", "react_wow", "react_thumbs", "quiet"]);
            await Assert.That(criteria.GetProperty("reply").GetString()).IsEqualTo("They have something to say about it, or it is addressed to them.");
            await Assert.That(criteria.GetProperty("react_laugh").GetString()).IsEqualTo("They find it funny but have nothing to add.");
            await Assert.That(criteria.GetProperty("react_love").GetString()).IsEqualTo("They like it or agree, without writing anything.");
            await Assert.That(criteria.GetProperty("react_wow").GetString()).IsEqualTo("It surprises or impresses them.");
            await Assert.That(criteria.GetProperty("react_thumbs").GetString()).IsEqualTo("A quick acknowledgement is enough.");
            await Assert.That(criteria.GetProperty("quiet").GetString()).IsEqualTo("It doesn't concern them; they scroll past.");
        }
        // One more question, about the conversation, in the same call.
        await Assert.That(root.GetProperty("questions").EnumerateObject().Select(p => p.Name))
            .IsEquivalentTo(["bot_0", "bot_1", "conversation_health"]);
        var health = root.GetProperty("questions").GetProperty("conversation_health");
        await Assert.That(health.GetProperty("type").GetString()).IsEqualTo("score");
        await Assert.That(health.GetProperty("instructions").GetString()).Contains("`flow`");
        await Assert.That(health.GetProperty("criteria").EnumerateArray().Select(c => c.GetString()!)).IsEquivalentTo(
        [
            "People are in the conversation and the bots add to it.",
            "The bots are mostly talking among themselves, but it is still on topic and fun to read.",
            "The bots are going in circles, repeating themselves, drifting off topic, or drowning out the people.",
        ]);

        // No steering: nothing about how many should answer, or about the others.
        await Assert.That(body.RootElement.GetRawText()).DoesNotContain("one or two");
    }

    [Test]
    public async Task Flow_CountsTheBotMessagesSinceAPersonLastWrote()
    {
        var jev = new FakeJev(_ => FakeJev.Chose(Quiet, 0.9));
        using var h = await OrchestratorHarness.CreateAsync(botCount: 2);
        await h.SayAsync("old news", at: DateTime.UtcNow.AddMinutes(-20));
        await h.SayAsync("what's the best pizza topping?", at: DateTime.UtcNow.AddSeconds(-90));
        await h.SayAsync("pineapple", asBot: h.Bots[0], at: DateTime.UtcNow.AddSeconds(-60));
        var message = await h.SayAsync("never", asBot: h.Bots[1], at: DateTime.UtcNow.AddSeconds(-30));

        await DecideAsync(h, jev, message);

        var flow = jev.Calls.Single().State.Flow;
        await Assert.That(flow.BotMessagesSinceLastHuman).IsEqualTo(2);
        await Assert.That(flow.SecondsSinceLastHuman!.Value).IsBetween(89, 95);
        await Assert.That(flow.HumansActive).IsTrue();
    }

    [Test]
    public async Task Flow_NobodyWroteLately_HumansAreNotActive()
    {
        var jev = new FakeJev(_ => FakeJev.Chose(Quiet, 0.9));
        using var h = await OrchestratorHarness.CreateAsync(botCount: 2);
        await h.SayAsync("pizza?", at: DateTime.UtcNow.AddMinutes(-10));
        var message = await h.SayAsync("still thinking about pizza", asBot: h.Bots[0]);

        await DecideAsync(h, jev, message);

        var flow = jev.Calls.Single().State.Flow;
        await Assert.That(flow.BotMessagesSinceLastHuman).IsEqualTo(1);
        await Assert.That(flow.HumansActive).IsFalse();
    }

    /// <summary>GPT and Claude want to answer a bot's message with <paramref name="p"/>; Gemini laughs.</summary>
    [Test]
    [Arguments(0.0, 0.8, 2)]
    [Arguments(0.99, 0.6, 2)]
    [Arguments(1.0, 0.9, 1)]
    [Arguments(1.2, 0.84, 0)]
    [Arguments(1.49, 0.85, 1)]
    [Arguments(1.5, 0.99, 0)]
    [Arguments(2.0, 0.99, 0)]
    public async Task Health_OnABotMessage_HoldsRepliesBack_ReactionsStay(double health, double p, int repliers)
    {
        var jev = new FakeJev(name => name == "Gemini" ? FakeJev.Chose(ReactLaugh, 0.7) : FakeJev.Chose(Reply, p)) { Health = _ => health };
        using var h = await OrchestratorHarness.CreateAsync(botCount: 4);
        await h.SayAsync("pizza?", at: DateTime.UtcNow.AddSeconds(-30));
        var message = await h.SayAsync("pineapple, obviously", asBot: h.Bots[3]);

        var verdicts = await DecideAsync(h, jev, message, bots: h.Bots[..3]);

        await Assert.That(verdicts.Count(v => v.Action == BotAction.Reply)).IsEqualTo(repliers);
        await Assert.That(verdicts.Single(v => v.Bot.Name == "Gemini").Action).IsEqualTo(BotAction.React);
    }

    [Test]
    public async Task Health_OnAPersonsMessage_ChangesNothing()
    {
        var jev = new FakeJev(_ => FakeJev.Chose(Reply, 0.7)) { Health = _ => 2.0 };
        using var h = await OrchestratorHarness.CreateAsync(botCount: 2);
        var message = await h.SayAsync("ok you two, which is it?");

        var verdicts = await DecideAsync(h, jev, message);

        await Assert.That(verdicts.All(v => v.Action == BotAction.Reply)).IsTrue();
    }

    [Test]
    public async Task HealthBoundaries_AreConfigurable()
    {
        var ranking = new BotRankingOptions { CautiousHealth = 0.3, SilentHealth = 0.8, CautiousReplyThreshold = 0.7, CautiousMaxReplies = 2 };
        using var h = await OrchestratorHarness.CreateAsync(botCount: 3);
        await h.SayAsync("pizza?", at: DateTime.UtcNow.AddSeconds(-30));
        var message = await h.SayAsync("pineapple", asBot: h.Bots[2]);

        var cautious = await DecideAsync(h, new FakeJev(_ => FakeJev.Chose(Reply, 0.75)) { Health = _ => 0.5 }, message, ranking, h.Bots[..2]);
        var silent = await DecideAsync(h, new FakeJev(_ => FakeJev.Chose(Reply, 0.99)) { Health = _ => 0.8 }, message, ranking, h.Bots[..2]);

        await Assert.That(cautious.Count(v => v.Action == BotAction.Reply)).IsEqualTo(2);
        await Assert.That(silent.Count(v => v.Action == BotAction.Reply)).IsEqualTo(0);
    }

    [Test]
    public async Task Health_IsLoggedForEveryDecision()
    {
        var logger = new ListLogger<BotDecisionEngine>();
        var jev = new FakeJev(_ => FakeJev.Chose(Quiet, 0.9)) { Health = _ => 1.234 };
        using var h = await OrchestratorHarness.CreateAsync(botCount: 2);
        await h.SayAsync("pizza?", at: DateTime.UtcNow.AddSeconds(-30));
        var message = await h.SayAsync("pineapple", asBot: h.Bots[0]);

        await new BotDecisionEngine(h.Db, jev, new BotRankingOptions(), logger).DecideAsync(h.Chat.Id, message, h.Bots);

        await Assert.That(logger.Entries.Any(e => e.Level == LogLevel.Information
            && e.Message.Contains("health 1.23") && e.Message.Contains("Cautious") && e.Message.Contains("1 bot messages since a person"))).IsTrue();
    }

    [Test]
    public async Task Health_FallsBackToTheProbabilities_WhenTheScoreIsMissing()
    {
        var logger = new ListLogger<BotDecisionEngine>();
        var decisions = Substitute.For<IDecisionsClient>();
        decisions.DecideAsync(default!, default!, default).ReturnsForAnyArgs(new DecisionsResponse("gen", "jev", new Dictionary<string, DecisionAnswer>
        {
            ["bot_0"] = FakeJev.Chose(Reply, 0.99),
            [HealthKey] = new("score", null, null, 0.8, new Dictionary<string, double> { ["0"] = 0.1, ["1"] = 0.1, ["2"] = 0.8 }),
        }, null));
        using var h = await OrchestratorHarness.CreateAsync(botCount: 2);
        await h.SayAsync("pizza?", at: DateTime.UtcNow.AddSeconds(-30));
        var message = await h.SayAsync("pineapple", asBot: h.Bots[1]);

        var verdicts = await new BotDecisionEngine(h.Db, decisions, new BotRankingOptions(), logger).DecideAsync(h.Chat.Id, message, h.Bots[..1]);

        // 0.1 * 1 + 0.8 * 2 = 1.7: silent.
        await Assert.That(verdicts.Single().Action).IsEqualTo(BotAction.Quiet);
        await Assert.That(logger.Entries.Any(e => e.Message.Contains("health 1.70"))).IsTrue();
    }

    [Test]
    public async Task Ceiling_StopsReplies_AfterTooManyBotMessagesSinceAPerson()
    {
        var jev = new FakeJev(_ => FakeJev.Chose(Reply, 0.99)) { Health = _ => 0 };
        using var h = await OrchestratorHarness.CreateAsync(botCount: 3);
        await h.SayAsync("pizza?", at: DateTime.UtcNow.AddMinutes(-1));
        for (var i = 0; i < 9; i++)
            await h.SayAsync($"take {i}", asBot: h.Bots[2], at: DateTime.UtcNow.AddSeconds(-50 + i));
        var message = await h.SayAsync("take 9", asBot: h.Bots[2]);

        // The tenth bot message since the person wrote: the ceiling is reached.
        var verdicts = await DecideAsync(h, jev, message, bots: h.Bots[..2]);

        await Assert.That(verdicts.All(v => v.Action == BotAction.Quiet)).IsTrue();
    }

    [Test]
    public async Task AReply_TellsJevWhoItAnswers()
    {
        var jev = new FakeJev(_ => FakeJev.Chose(Quiet, 0.9));
        using var h = await OrchestratorHarness.CreateAsync(botCount: 2);
        var said = await h.SayAsync("pineapple belongs on pizza", asBot: h.Bots[1], at: DateTime.UtcNow.AddMinutes(-1));
        var message = await h.SayAsync("no way", replyToId: said.Id);

        await DecideAsync(h, jev, message);

        await Assert.That(jev.Calls.Single().State.NewMessage.ReplyTo).IsEqualTo("Claude");
    }

    [Test]
    [Arguments("reply", 0.81, BotAction.Reply, null)]
    [Arguments("reply", 0.6, BotAction.Reply, null)]
    [Arguments("reply", 0.59, BotAction.Quiet, null)]
    [Arguments("react_wow", 0.55, BotAction.React, "😮")]
    [Arguments("react_laugh", 0.5, BotAction.React, "😂")]
    [Arguments("react_love", 0.7, BotAction.React, "❤️")]
    [Arguments("react_thumbs", 0.9, BotAction.React, "👍")]
    [Arguments("react_thumbs", 0.49, BotAction.Quiet, null)]
    [Arguments("quiet", 0.64, BotAction.Quiet, null)]
    public async Task Probabilities_MapToOneAction(string choice, double p, BotAction action, string? emoji)
    {
        var jev = new FakeJev(_ => FakeJev.Chose(choice, p));
        using var h = await OrchestratorHarness.CreateAsync();
        var message = await h.SayAsync("anyone seen the new dune movie?");

        var verdict = (await DecideAsync(h, jev, message)).Single();

        await Assert.That(verdict.Action).IsEqualTo(action);
        await Assert.That(verdict.Emoji).IsEqualTo(emoji);
    }

    [Test]
    public async Task Thresholds_AreConfigurable()
    {
        var jev = new FakeJev(name => name == "GPT" ? FakeJev.Chose(Reply, 0.5) : FakeJev.Chose(ReactWow, 0.3));
        using var h = await OrchestratorHarness.CreateAsync(botCount: 2);
        var message = await h.SayAsync("whoa");

        var verdicts = await DecideAsync(h, jev, message, new BotRankingOptions { ReplyThreshold = 0.4, ReactThreshold = 0.2 });

        await Assert.That(verdicts[0].Action).IsEqualTo(BotAction.Reply);
        await Assert.That(verdicts[1].Action).IsEqualTo(BotAction.React);
    }

    [Test]
    public async Task ReplyCap_KeepsTheLikeliestRepliers()
    {
        var p = new Dictionary<string, double> { ["GPT"] = 0.7, ["Claude"] = 0.95, ["Gemini"] = 0.65, ["Llama"] = 0.9 };
        var jev = new FakeJev(name => FakeJev.Chose(Reply, p[name]));
        using var h = await OrchestratorHarness.CreateAsync(botCount: 4);
        var message = await h.SayAsync("everyone, dune: yes or no?");

        var verdicts = await DecideAsync(h, jev, message, new BotRankingOptions { MaxReplies = 2 });

        await Assert.That(verdicts.Where(v => v.Action == BotAction.Reply).Select(v => v.Bot.Name)).IsEquivalentTo(["Claude", "Llama"]);
    }

    [Test]
    public async Task ReplyCap_DefaultsToFive()
    {
        await Assert.That(new BotRankingOptions().MaxReplies).IsEqualTo(5);
        await Assert.That(BotRankingOptions.FromConfiguration(new ConfigurationBuilder().Build()).MaxReplies).IsEqualTo(5);
    }

    [Test]
    public async Task Configuration_SetsThresholdsCapAndEmoji()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["BotRanking:ReplyThreshold"] = "0.7",
            ["BotRanking:ReactThreshold"] = "0.4",
            ["BotRanking:MaxReplies"] = "3",
            ["BotRanking:Emojis:react_love"] = "🥰",
            ["BotRanking:CautiousHealth"] = "0.8",
            ["BotRanking:SilentHealth"] = "1.2",
            ["BotRanking:CautiousReplyThreshold"] = "0.9",
            ["BotRanking:CautiousMaxReplies"] = "2",
            ["BotRanking:HumansActiveMinutes"] = "10",
        }).Build();

        var options = BotRankingOptions.FromConfiguration(config);

        await Assert.That(options.ReplyThreshold).IsEqualTo(0.7);
        await Assert.That(options.ReactThreshold).IsEqualTo(0.4);
        await Assert.That(options.MaxReplies).IsEqualTo(3);
        await Assert.That(options.Emojis[ReactLove]).IsEqualTo("🥰");
        await Assert.That(options.Emojis[ReactLaugh]).IsEqualTo("😂");
        await Assert.That(options.CautiousHealth).IsEqualTo(0.8);
        await Assert.That(options.SilentHealth).IsEqualTo(1.2);
        await Assert.That(options.CautiousReplyThreshold).IsEqualTo(0.9);
        await Assert.That(options.CautiousMaxReplies).IsEqualTo(2);
        await Assert.That(options.HumansActiveWindow).IsEqualTo(TimeSpan.FromMinutes(10));
    }

    [Test]
    public async Task HealthDefaults_MatchTheIssue()
    {
        var options = BotRankingOptions.FromConfiguration(new ConfigurationBuilder().Build());
        await Assert.That(options.CautiousHealth).IsEqualTo(1.0);
        await Assert.That(options.SilentHealth).IsEqualTo(1.5);
        await Assert.That(options.CautiousReplyThreshold).IsEqualTo(0.85);
        await Assert.That(options.CautiousMaxReplies).IsEqualTo(1);
        await Assert.That(new NeuralDamage.Infrastructure.Services.BotBehaviorOptions().MaxBotMessagesPerPersonMessage).IsEqualTo(10);
    }

    [Test]
    public async Task RateCappedBot_SitsTheMessageOut()
    {
        var jev = new FakeJev(_ => FakeJev.Chose(Reply, 0.9));
        using var h = await OrchestratorHarness.CreateAsync(botCount: 2);
        var earlier = await h.SayAsync("go", at: DateTime.UtcNow.AddSeconds(-50));
        for (var i = 0; i < 4; i++)
            await h.SayAsync($"reply {i}", asBot: h.Bots[0], replyToId: earlier.Id, at: DateTime.UtcNow.AddSeconds(-40 + i));
        var message = await h.SayAsync("and now?");

        var verdicts = await DecideAsync(h, jev, message);

        await Assert.That(verdicts[0].Action).IsEqualTo(BotAction.Quiet);
        await Assert.That(verdicts[1].Action).IsEqualTo(BotAction.Reply);
    }

    [Test]
    public async Task JevUnavailable_OnlyMentionedOrRepliedToBotsReply_NobodyReacts()
    {
        var jev = new FakeJev { Unavailable = true };
        using var h = await OrchestratorHarness.CreateAsync(botCount: 4);
        var claudeSaid = await h.SayAsync("pineapple belongs on pizza", asBot: h.Bots[1], at: DateTime.UtcNow.AddMinutes(-1));
        var message = await h.SayAsync("@GPT what do you think? gemini, you too", replyToId: claudeSaid.Id);

        var verdicts = await DecideAsync(h, jev, message);

        await Assert.That(jev.Calls).HasSingleItem();
        // Named without the @ is not enough for the fallback.
        await Assert.That(verdicts.Where(v => v.Action == BotAction.Reply).Select(v => v.Bot.Name)).IsEquivalentTo(["GPT", "Claude"]);
        await Assert.That(verdicts.Any(v => v.Action == BotAction.React)).IsFalse();
    }

    [Test]
    public async Task JevThrowing_FallsBackToo()
    {
        var decisions = Substitute.For<IDecisionsClient>();
        decisions.DecideAsync(default!, default!, default).ReturnsForAnyArgs<DecisionsResponse?>(_ => throw new HttpRequestException("down"));
        using var h = await OrchestratorHarness.CreateAsync(botCount: 2);
        var message = await h.SayAsync("@claude hi");

        var verdicts = await new BotDecisionEngine(h.Db, decisions, new BotRankingOptions(), NullLogger<BotDecisionEngine>.Instance)
            .DecideAsync(h.Chat.Id, message, h.Bots);

        await Assert.That(verdicts.Where(v => v.Action == BotAction.Reply).Select(v => v.Bot.Name)).IsEquivalentTo(["Claude"]);
    }

    [Test]
    public async Task JevUnavailable_ABotsMessage_PicksNobody()
    {
        var jev = new FakeJev { Unavailable = true };
        using var h = await OrchestratorHarness.CreateAsync(botCount: 2);
        var gptSaid = await h.SayAsync("hot take", asBot: h.Bots[0], at: DateTime.UtcNow.AddMinutes(-1));
        // Every bot reply links what it answered; that is not addressing GPT.
        // Nor is a bot's @mention: without Jev, bots never answer bots.
        var message = await h.SayAsync("colder take, @GPT", asBot: h.Bots[1], replyToId: gptSaid.Id);

        var verdicts = await DecideAsync(h, jev, message);

        await Assert.That(verdicts.All(v => v.Action == BotAction.Quiet)).IsTrue();
    }

    [Test]
    public async Task JevUnavailable_ThroughTheOrchestrator_TheRepliedToBotAnswers()
    {
        var jev = new FakeJev { Unavailable = true };
        using var h = await OrchestratorHarness.CreateAsync(botCount: 2, configure: jev.Engine());
        var claudeSaid = await h.SayAsync("pineapple belongs on pizza", asBot: h.Bots[1], at: DateTime.UtcNow.AddMinutes(-10));
        h.Reply("it absolutely does");
        var reply = await h.SayAsync("no way", replyToId: claudeSaid.Id);

        await h.Orchestrator.ProcessMessageAsync(h.Chat.Id, reply.Id);

        await h.Notifications.Received(1).NotifyMessageNew(h.Chat.Id, Arg.Is<MessageDto>(m => m.SenderBotId == h.Bots[1].Id));
        await h.Notifications.DidNotReceive().NotifyMessageNew(h.Chat.Id, Arg.Is<MessageDto>(m => m.SenderBotId == h.Bots[0].Id));
        await Assert.That(await h.Db.Reactions.AnyAsync()).IsFalse();
    }

    [Test]
    [Arguments("hey @gpt", true)]
    [Arguments("@GPT, thoughts?", true)]
    [Arguments("ask @chatgpt", true)]
    [Arguments("hey gpt", false)]
    [Arguments("mail me at x@gpt.com", false)]
    [Arguments("@gpt4", false)]
    public async Task IsMentioned_NeedsTheAt(string content, bool expected)
    {
        var bot = new Bot { Name = "GPT", ModelId = "m", CreatedById = Guid.NewGuid(), Aliases = "chatgpt" };
        await Assert.That(BotDecisionEngine.IsMentioned(content, bot)).IsEqualTo(expected);
    }
}
