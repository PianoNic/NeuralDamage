using NeuralDamage.Application.Services.BotDecision;

namespace NeuralDamage.Tests.BotDecision;

public class Tier2WeightedScoreTests
{
    private static Tier2Context DefaultContext(
        bool isQuestion = false,
        int botMessages = 0,
        int totalMessages = 20,
        int secondsSince = -1,
        int messageLength = 50,
        int totalBots = 2) =>
        new(isQuestion, botMessages, totalMessages, secondsSince, messageLength, totalBots);

    [Fact]
    public void BaseScore_IsPositive()
    {
        var score = Tier2WeightedScore.ComputeScore(DefaultContext());
        Assert.True(score > 0);
    }

    [Fact]
    public void GroupQuestion_IncreasesScore()
    {
        var without = Tier2WeightedScore.ComputeScore(DefaultContext(isQuestion: false));
        var with = Tier2WeightedScore.ComputeScore(DefaultContext(isQuestion: true));
        Assert.True(with > without);
    }

    [Fact]
    public void RecentlySpokeUnder30s_HeavyPenalty()
    {
        var normal = Tier2WeightedScore.ComputeScore(DefaultContext(secondsSince: 300));
        var recent = Tier2WeightedScore.ComputeScore(DefaultContext(secondsSince: 10));
        Assert.True(normal > recent);
    }

    [Fact]
    public void RecentlySpoke30To120s_ModeratePenalty()
    {
        var normal = Tier2WeightedScore.ComputeScore(DefaultContext(secondsSince: 300));
        var moderate = Tier2WeightedScore.ComputeScore(DefaultContext(secondsSince: 60));
        Assert.True(normal > moderate);
    }

    [Fact]
    public void DominatingConversation_Penalty()
    {
        var normal = Tier2WeightedScore.ComputeScore(DefaultContext(botMessages: 2, totalMessages: 20));
        var dominant = Tier2WeightedScore.ComputeScore(DefaultContext(botMessages: 10, totalMessages: 20));
        Assert.True(normal > dominant);
    }

    [Fact]
    public void ShortMessage_Penalty()
    {
        var normal = Tier2WeightedScore.ComputeScore(DefaultContext(messageLength: 50));
        var short_ = Tier2WeightedScore.ComputeScore(DefaultContext(messageLength: 5));
        Assert.True(normal > short_);
    }

    [Fact]
    public void ManyBots_Penalty()
    {
        var few = Tier2WeightedScore.ComputeScore(DefaultContext(totalBots: 1));
        var many = Tier2WeightedScore.ComputeScore(DefaultContext(totalBots: 5));
        Assert.True(few > many);
    }

    [Fact]
    public void Score_NeverNegative()
    {
        // Worst case: recently spoke, dominating, short message, many bots
        var score = Tier2WeightedScore.ComputeScore(new Tier2Context(
            IsGroupQuestion: false,
            BotMessagesInLast20: 15,
            TotalRecentMessages: 20,
            SecondsSinceLastBotMessage: 5,
            MessageLength: 2,
            TotalBotsInChat: 10));
        Assert.True(score >= 0.0);
    }

    [Fact]
    public void Score_NeverAboveOne()
    {
        // Best case: question, never spoke, long message, solo bot
        var score = Tier2WeightedScore.ComputeScore(new Tier2Context(
            IsGroupQuestion: true,
            BotMessagesInLast20: 0,
            TotalRecentMessages: 20,
            SecondsSinceLastBotMessage: -1,
            MessageLength: 500,
            TotalBotsInChat: 1));
        Assert.True(score <= 1.0);
    }

    [Fact]
    public void QuestionFromFreshBot_HigherThanNormal()
    {
        var questionScore = Tier2WeightedScore.ComputeScore(new Tier2Context(
            IsGroupQuestion: true,
            BotMessagesInLast20: 0,
            TotalRecentMessages: 10,
            SecondsSinceLastBotMessage: -1,
            MessageLength: 80,
            TotalBotsInChat: 1));
        var normalScore = Tier2WeightedScore.ComputeScore(new Tier2Context(
            IsGroupQuestion: false,
            BotMessagesInLast20: 0,
            TotalRecentMessages: 10,
            SecondsSinceLastBotMessage: -1,
            MessageLength: 80,
            TotalBotsInChat: 1));
        Assert.True(questionScore > normalScore);
        Assert.True(questionScore > 0.3); // in the "undecided" zone, closer to respond
    }

    [Fact]
    public void ShortOkMessage_BelowSkipThreshold()
    {
        var score = Tier2WeightedScore.ComputeScore(new Tier2Context(
            IsGroupQuestion: false,
            BotMessagesInLast20: 5,
            TotalRecentMessages: 10,
            SecondsSinceLastBotMessage: 20,
            MessageLength: 2,
            TotalBotsInChat: 3));
        Assert.True(score <= Tier2WeightedScore.SkipThreshold);
    }
}
