using NeuralDamage.Domain;

namespace NeuralDamage.Infrastructure.Services.Feed;

/// <summary>
/// When a bot posts on its own. Each bot spreads its daily posts over its own
/// waking hours, in the app's time zone, with a little chance in every tick
/// so that they do not land on the minute.
/// </summary>
public static class FeedSchedule
{
    /// <summary>The bots that may post on their own at all: public, alive, with the feed on and a cap above zero.</summary>
    public static bool MayPost(Bot bot) => bot.IsActive && bot.IsPublic && bot.FeedEnabled && bot.MaxPostsPerDay > 0;

    /// <summary>Midnight today in the bots' time zone, as UTC: what "posts today" counts from.</summary>
    public static DateTime StartOfDay(DateTimeOffset now, BotClock clock)
    {
        var local = clock.Local(now);
        var midnight = new DateTimeOffset(local.Year, local.Month, local.Day, 0, 0, 0, local.Offset);
        return midnight.UtcDateTime;
    }

    /// <summary>The local hour a bot starts its day: the configured start, plus up to the spread, fixed per bot.</summary>
    public static int WakeHour(Guid botId, FeedOptions options)
    {
        var spread = Math.Max(0, options.WakeSpreadHours);
        return options.DayStartHour + (spread == 0 ? 0 : (int)((uint)botId.GetHashCode() % (uint)(spread + 1)));
    }

    /// <summary>
    /// Whether <paramref name="bot"/> posts in this tick. Never past its cap
    /// for the day or outside its waking hours; never sooner than three
    /// quarters of its spacing (its waking hours over its cap) after its last
    /// post; and otherwise with a chance that makes one post per spacing likely.
    /// </summary>
    /// <param name="roll">A random number in [0, 1).</param>
    public static bool IsDue(Bot bot, int postsToday, DateTime? lastPostUtc, DateTimeOffset now, BotClock clock, FeedOptions options, double roll)
    {
        if (!MayPost(bot) || postsToday >= bot.MaxPostsPerDay)
            return false;

        var hour = clock.Local(now).Hour;
        var wake = WakeHour(bot.Id, options);
        if (hour < wake || hour >= options.DayEndHour)
            return false;

        var spacing = TimeSpan.FromHours(Math.Max(1, options.DayEndHour - wake)) / bot.MaxPostsPerDay;
        if (lastPostUtc is { } last && now.UtcDateTime - last < spacing * 0.75)
            return false;

        var chance = Math.Clamp(options.Tick / (spacing * 0.5), 0, 1);
        return roll < chance;
    }
}
