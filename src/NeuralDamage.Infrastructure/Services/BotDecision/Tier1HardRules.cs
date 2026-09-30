using NeuralDamage.Domain;

namespace NeuralDamage.Infrastructure.Services.BotDecision;

public enum Tier1Result
{
    MustRespond,
    MustSkip,
    Undecided,

    /// <summary>
    /// Said to the whole room. Only some of the addressed bots answer; the
    /// decision engine picks which.
    /// </summary>
    GroupAddressed
}

public static class Tier1HardRules
{
    public static Tier1Result Evaluate(Message message, Bot bot, bool isMuted, bool isStopped)
    {
        // Inactive bot never responds
        if (!bot.IsActive)
            return Tier1Result.MustSkip;

        // Muted or stopped chat
        if (isMuted || isStopped)
            return Tier1Result.MustSkip;

        // Slash commands never trigger bots
        if (message.Content.StartsWith('/'))
            return Tier1Result.MustSkip;

        // Bot-to-bot: only respond if explicitly mentioned
        if (message.SenderBotId is not null)
        {
            var mentioned = FuzzyNameMatcher.IsNameMentioned(message.Content, bot.Name, bot.Aliases);
            return mentioned ? Tier1Result.MustRespond : Tier1Result.MustSkip;
        }

        // Name mentioned in message
        if (FuzzyNameMatcher.IsNameMentioned(message.Content, bot.Name, bot.Aliases))
            return Tier1Result.MustRespond;

        // Reply to this bot's message
        if (message.ReplyToId is not null && message.ReplyTo?.SenderBotId == bot.Id)
            return Tier1Result.MustRespond;

        // Group address ("everyone", "all bots", etc.) - checked after the
        // direct forms so "GPT, and everyone else" still counts as GPT's.
        if (FuzzyNameMatcher.IsGroupAddress(message.Content))
            return Tier1Result.GroupAddressed;

        return Tier1Result.Undecided;
    }
}
