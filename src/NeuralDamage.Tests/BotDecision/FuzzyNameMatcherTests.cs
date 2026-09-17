using NeuralDamage.Infrastructure.Services.BotDecision;

namespace NeuralDamage.Tests.BotDecision;

public class FuzzyNameMatcherTests
{
    [Test]
    [Arguments("hey gpt what do you think?", "GPT-4o", null, true)]
    [Arguments("ask sarah about it", "Sassy Sarah", null, true)]
    [Arguments("einstein would know this", "Professor Einstein", null, true)]
    [Arguments("claude can you help?", "Claude Helper", null, true)]
    [Arguments("What does ND think?", "Neural Damage Bot", "ND", true)]
    [Arguments("yo prof help me out", "Professor Einstein", "prof", true)]
    public async Task IsNameMentioned_Matches(string message, string botName, string? aliases, bool expected)
    {
        await Assert.That(FuzzyNameMatcher.IsNameMentioned(message, botName, aliases)).IsEqualTo(expected);
    }

    [Test]
    [Arguments("I like claudette's work", "Claude Helper", null)]
    [Arguments("the algorithm is smart", "Al Bot", "algo")]
    [Arguments("what a damaged reputation", "Reporter Bot", null)]
    public async Task IsNameMentioned_DoesNotFalsePositive(string message, string botName, string? aliases)
    {
        await Assert.That(FuzzyNameMatcher.IsNameMentioned(message, botName, aliases)).IsFalse();
    }

    [Test]
    public async Task IsNameMentioned_EmptyMessage_ReturnsFalse()
    {
        await Assert.That(FuzzyNameMatcher.IsNameMentioned("", "Bot", null)).IsFalse();
        await Assert.That(FuzzyNameMatcher.IsNameMentioned("  ", "Bot", null)).IsFalse();
    }

    [Test]
    public async Task IsNameMentioned_SingleCharNameWord_Skipped()
    {
        // Bot name "A Bot" — the "A" word should be skipped (too short)
        // but "Bot" should still match
        await Assert.That(FuzzyNameMatcher.IsNameMentioned("hey bot", "A Bot", null)).IsTrue();
        await Assert.That(FuzzyNameMatcher.IsNameMentioned("a message", "A Bot", null)).IsFalse();
    }

    [Test]
    public async Task IsNameMentioned_MultipleAliases()
    {
        await Assert.That(FuzzyNameMatcher.IsNameMentioned("ask chatgpt", "GPT Model", "gpt,chatgpt,openai")).IsTrue();
        await Assert.That(FuzzyNameMatcher.IsNameMentioned("hey openai", "GPT Model", "gpt,chatgpt,openai")).IsTrue();
    }

    [Test]
    public async Task FindNameMatch_ReturnsMatchedTerm()
    {
        var (matched, term) = FuzzyNameMatcher.FindNameMatch("hey sarah", "Sassy Sarah", null);
        await Assert.That(matched).IsTrue();
        await Assert.That(term).IsEqualTo("sarah", StringComparer.OrdinalIgnoreCase);
    }

    [Test]
    [Arguments("hey everyone", true)]
    [Arguments("all bots respond", true)]
    [Arguments("you guys are smart", true)]
    [Arguments("y'all need to chill", true)]
    [Arguments("just a normal message", false)]
    [Arguments("what do you think?", false)]
    public async Task IsGroupAddress_DetectsCorrectly(string message, bool expected)
    {
        await Assert.That(FuzzyNameMatcher.IsGroupAddress(message)).IsEqualTo(expected);
    }
}
