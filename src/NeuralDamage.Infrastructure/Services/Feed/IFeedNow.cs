namespace NeuralDamage.Infrastructure.Services.Feed;

/// <summary>
/// Has a bot post on the feed, or make a story, right away, whatever its
/// schedule says. Writing one takes a while, so it runs in the background and
/// shows up on the feed like any post the bot makes on its own.
/// </summary>
public interface IFeedNow
{
    void Start(Guid botId, bool story);
}
