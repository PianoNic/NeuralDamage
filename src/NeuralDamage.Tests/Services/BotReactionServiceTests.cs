using NeuralDamage.Infrastructure.Services;

namespace NeuralDamage.Tests.Services;

public class BotReactionServiceTests
{
    [Test]
    [Arguments("that's so funny lol", "😂")]
    [Arguments("I love this", "❤️")]
    [Arguments("I agree with you", "👍")]
    [Arguments("wow that's incredible", "😮")]
    [Arguments("that's so sad", "😢")]
    [Arguments("thanks for helping", "🙏")]
    [Arguments("this is fire", "🔥")]
    [Arguments("perfect execution", "💯")]
    public async Task SelectEmoji_MatchesKeywords(string message, string expectedEmoji)
    {
        var emoji = BotReactionService.SelectEmoji(message);
        await Assert.That(emoji).IsEqualTo(expectedEmoji);
    }

    [Test]
    public async Task SelectEmoji_NoKeywords_ReturnsFallback()
    {
        var emoji = BotReactionService.SelectEmoji("the quick brown fox jumps over the lazy dog");
        await Assert.That(emoji).IsNotNull();
        await Assert.That(new[] { "👍", "❤️", "😂", "🔥" }).Contains(emoji);
    }

    [Test]
    public async Task ShouldReact_ReturnsBool()
    {
        // Run many times to verify it returns both true and false
        var results = Enumerable.Range(0, 1000).Select(_ => BotReactionService.ShouldReact()).ToList();
        await Assert.That(results).Contains(true);
        await Assert.That(results).Contains(false);
        // ~15% should be true (allow wide margin)
        var trueCount = results.Count(r => r);
        await Assert.That(trueCount).IsBetween(50, 300);
    }
}
