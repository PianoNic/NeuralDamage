using NeuralDamage.Infrastructure.Services.Feed;

namespace NeuralDamage.Tests.Feed;

/// <summary>A feed lives on pictures: most posts are offered one, and the offer makes it the norm.</summary>
public class FeedPictureShareTests
{
    [Test]
    public async Task MostPostsAreOfferedAPicture_ByDefault()
    {
        await Assert.That(new FeedOptions().ImageShare).IsGreaterThanOrEqualTo(0.8);
    }

    [Test]
    public async Task TheOffer_MakesAPictureTheNorm_AndNoneTheException()
    {
        var instruction = FeedPostOutput.Instruction(canIncludeMe: true, canGenerate: true, canSearch: true);

        await Assert.That(instruction).Contains("This post comes with a picture");
        await Assert.That(instruction).Contains("Only if a picture really doesn't fit");
        await Assert.That(instruction).DoesNotContain("You may add one picture");
    }
}
