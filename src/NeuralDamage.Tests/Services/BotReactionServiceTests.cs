using NeuralDamage.Application.Services;

namespace NeuralDamage.Tests.Services;

public class BotReactionServiceTests
{
    [Theory]
    [InlineData("that's so funny lol", "😂")]
    [InlineData("I love this", "❤️")]
    [InlineData("I agree with you", "👍")]
    [InlineData("wow that's incredible", "😮")]
    [InlineData("that's so sad", "😢")]
    [InlineData("thanks for helping", "🙏")]
    [InlineData("this is fire", "🔥")]
    [InlineData("perfect execution", "💯")]
    public void SelectEmoji_MatchesKeywords(string message, string expectedEmoji)
    {
        var emoji = BotReactionService.SelectEmoji(message);
        Assert.Equal(expectedEmoji, emoji);
    }

    [Fact]
    public void SelectEmoji_NoKeywords_ReturnsFallback()
    {
        var emoji = BotReactionService.SelectEmoji("the quick brown fox jumps over the lazy dog");
        Assert.NotNull(emoji);
        Assert.Contains(emoji, new[] { "👍", "❤️", "😂", "🔥" });
    }

    [Fact]
    public void ShouldReact_ReturnsBool()
    {
        // Run many times to verify it returns both true and false
        var results = Enumerable.Range(0, 1000).Select(_ => BotReactionService.ShouldReact()).ToList();
        Assert.Contains(true, results);
        Assert.Contains(false, results);
        // ~15% should be true (allow wide margin)
        var trueCount = results.Count(r => r);
        Assert.InRange(trueCount, 50, 300);
    }
}
