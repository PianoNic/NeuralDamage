using NeuralDamage.Infrastructure.Services.BotDecision;

namespace NeuralDamage.Tests.BotDecision;

public class Tier2WeightedScoreTests
{
    private static Tier2Context DefaultContext(
        bool isQuestion = false,
        int botMessages = 0,
        int totalMessages = 20,
        bool isContinuation = false,
        int messageLength = 50,
        int totalBots = 2) =>
        new(isQuestion, botMessages, totalMessages, isContinuation, messageLength, totalBots);

    [Test]
    public async Task BaseScore_IsPositive()
    {
        var score = Tier2WeightedScore.ComputeScore(DefaultContext());
        await Assert.That(score > 0).IsTrue();
    }

    [Test]
    public async Task GroupQuestion_IncreasesScore()
    {
        var without = Tier2WeightedScore.ComputeScore(DefaultContext(isQuestion: false));
        var with = Tier2WeightedScore.ComputeScore(DefaultContext(isQuestion: true));
        await Assert.That(with > without).IsTrue();
    }

    [Test]
    public async Task Continuation_IncreasesScore()
    {
        var fresh = Tier2WeightedScore.ComputeScore(DefaultContext());
        var continuing = Tier2WeightedScore.ComputeScore(DefaultContext(isContinuation: true));
        await Assert.That(continuing > fresh).IsTrue();
    }

    [Test]
    public async Task Continuation_OrdinaryReply_ClearsRespondThreshold()
    {
        // The bot you are talking to must not go quiet mid-conversation.
        var score = Tier2WeightedScore.ComputeScore(DefaultContext(isContinuation: true, botMessages: 3));
        await Assert.That(score >= Tier2WeightedScore.RespondThreshold).IsTrue();
    }

    [Test]
    public async Task DominatingConversation_Penalty()
    {
        var normal = Tier2WeightedScore.ComputeScore(DefaultContext(botMessages: 2, totalMessages: 20));
        var dominant = Tier2WeightedScore.ComputeScore(DefaultContext(botMessages: 10, totalMessages: 20));
        await Assert.That(normal > dominant).IsTrue();
    }

    [Test]
    public async Task ShortMessage_Penalty()
    {
        var normal = Tier2WeightedScore.ComputeScore(DefaultContext(messageLength: 50));
        var short_ = Tier2WeightedScore.ComputeScore(DefaultContext(messageLength: 5));
        await Assert.That(normal > short_).IsTrue();
    }

    [Test]
    public async Task ManyBots_Penalty()
    {
        var few = Tier2WeightedScore.ComputeScore(DefaultContext(totalBots: 1));
        var many = Tier2WeightedScore.ComputeScore(DefaultContext(totalBots: 5));
        await Assert.That(few > many).IsTrue();
    }

    [Test]
    public async Task Score_NeverNegative()
    {
        // Worst case: dominating, short message, many bots
        var score = Tier2WeightedScore.ComputeScore(new Tier2Context(
            IsGroupQuestion: false,
            BotMessagesInLast20: 15,
            TotalRecentMessages: 20,
            IsContinuation: false,
            MessageLength: 2,
            TotalBotsInChat: 10));
        await Assert.That(score >= 0.0).IsTrue();
    }

    [Test]
    public async Task Score_NeverAboveOne()
    {
        // Best case: question, continuing, long message, solo bot
        var score = Tier2WeightedScore.ComputeScore(new Tier2Context(
            IsGroupQuestion: true,
            BotMessagesInLast20: 0,
            TotalRecentMessages: 20,
            IsContinuation: false,
            MessageLength: 500,
            TotalBotsInChat: 1));
        await Assert.That(score <= 1.0).IsTrue();
    }

    [Test]
    public async Task QuestionFromFreshBot_HigherThanNormal()
    {
        var questionScore = Tier2WeightedScore.ComputeScore(new Tier2Context(
            IsGroupQuestion: true,
            BotMessagesInLast20: 0,
            TotalRecentMessages: 10,
            IsContinuation: false,
            MessageLength: 80,
            TotalBotsInChat: 1));
        var normalScore = Tier2WeightedScore.ComputeScore(new Tier2Context(
            IsGroupQuestion: false,
            BotMessagesInLast20: 0,
            TotalRecentMessages: 10,
            IsContinuation: false,
            MessageLength: 80,
            TotalBotsInChat: 1));
        await Assert.That(questionScore > normalScore).IsTrue();
        await Assert.That(questionScore > 0.3).IsTrue(); // in the "undecided" zone, closer to respond
    }

    [Test]
    public async Task ShortOkMessage_BelowSkipThreshold()
    {
        var score = Tier2WeightedScore.ComputeScore(new Tier2Context(
            IsGroupQuestion: false,
            BotMessagesInLast20: 5,
            TotalRecentMessages: 10,
            IsContinuation: false,
            MessageLength: 2,
            TotalBotsInChat: 3));
        await Assert.That(score <= Tier2WeightedScore.SkipThreshold).IsTrue();
    }
}
