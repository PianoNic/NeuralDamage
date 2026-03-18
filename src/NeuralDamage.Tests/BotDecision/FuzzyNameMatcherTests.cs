using NeuralDamage.Application.Services.BotDecision;

namespace NeuralDamage.Tests.BotDecision;

public class FuzzyNameMatcherTests
{
    [Theory]
    [InlineData("hey gpt what do you think?", "GPT-4o", null, true)]
    [InlineData("ask sarah about it", "Sassy Sarah", null, true)]
    [InlineData("einstein would know this", "Professor Einstein", null, true)]
    [InlineData("claude can you help?", "Claude Helper", null, true)]
    [InlineData("What does ND think?", "Neural Damage Bot", "ND", true)]
    [InlineData("yo prof help me out", "Professor Einstein", "prof", true)]
    public void IsNameMentioned_Matches(string message, string botName, string? aliases, bool expected)
    {
        Assert.Equal(expected, FuzzyNameMatcher.IsNameMentioned(message, botName, aliases));
    }

    [Theory]
    [InlineData("I like claudette's work", "Claude Helper", null)]
    [InlineData("the algorithm is smart", "Al Bot", "algo")]
    [InlineData("what a damaged reputation", "Reporter Bot", null)]
    public void IsNameMentioned_DoesNotFalsePositive(string message, string botName, string? aliases)
    {
        Assert.False(FuzzyNameMatcher.IsNameMentioned(message, botName, aliases));
    }

    [Fact]
    public void IsNameMentioned_EmptyMessage_ReturnsFalse()
    {
        Assert.False(FuzzyNameMatcher.IsNameMentioned("", "Bot", null));
        Assert.False(FuzzyNameMatcher.IsNameMentioned("  ", "Bot", null));
    }

    [Fact]
    public void IsNameMentioned_SingleCharNameWord_Skipped()
    {
        // Bot name "A Bot" — the "A" word should be skipped (too short)
        // but "Bot" should still match
        Assert.True(FuzzyNameMatcher.IsNameMentioned("hey bot", "A Bot", null));
        Assert.False(FuzzyNameMatcher.IsNameMentioned("a message", "A Bot", null));
    }

    [Fact]
    public void IsNameMentioned_MultipleAliases()
    {
        Assert.True(FuzzyNameMatcher.IsNameMentioned("ask chatgpt", "GPT Model", "gpt,chatgpt,openai"));
        Assert.True(FuzzyNameMatcher.IsNameMentioned("hey openai", "GPT Model", "gpt,chatgpt,openai"));
    }

    [Fact]
    public void FindNameMatch_ReturnsMatchedTerm()
    {
        var (matched, term) = FuzzyNameMatcher.FindNameMatch("hey sarah", "Sassy Sarah", null);
        Assert.True(matched);
        Assert.Equal("sarah", term, StringComparer.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("hey everyone", true)]
    [InlineData("all bots respond", true)]
    [InlineData("you guys are smart", true)]
    [InlineData("y'all need to chill", true)]
    [InlineData("just a normal message", false)]
    [InlineData("what do you think?", false)]
    public void IsGroupAddress_DetectsCorrectly(string message, bool expected)
    {
        Assert.Equal(expected, FuzzyNameMatcher.IsGroupAddress(message));
    }
}
