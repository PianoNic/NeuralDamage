using NeuralDamage.Infrastructure.Dtos;
using NeuralDamage.Domain;
using NeuralDamage.Infrastructure.Services;

namespace NeuralDamage.Infrastructure.Mappers;

public static class BotMapper
{
    /// <summary>
    /// Projects bots with their creator and usage counts. The counts are
    /// correlated subqueries, so a whole list is still one round trip.
    /// The looks and reference pictures are filtered in the query: only
    /// <paramref name="viewerId"/>'s own bots carry them.
    /// </summary>
    public static IQueryable<BotDto> SelectDto(this IQueryable<Bot> bots, DateTime utcNow, Guid? viewerId)
    {
        var today = utcNow.Date;
        var weekAgo = utcNow.AddDays(-7);
        return bots.Select(b => new BotDto(
            b.Id, b.Name, b.ModelId, b.SystemPrompt, b.Personality, b.Temperature, b.AvatarUrl, b.Aliases,
            b.CreatedById, b.IsActive, b.CreatedAt, b.IsPublic, b.ChatId,
            new BotCreatorDto(b.CreatedBy.Id, b.CreatedBy.DisplayName),
            b.ChatMembers.Count(),
            b.Messages.Count(m => m.CreatedAt >= today),
            b.Messages.Count(m => m.CreatedAt >= weekAgo),
            ModelStatus.Available,
            null,
            b.FeedEnabled,
            b.MaxPostsPerDay,
            b.MaxStoriesPerDay,
            b.CreatedById == viewerId ? b.Looks : null,
            b.References
                .Where(r => b.CreatedById == viewerId)
                .OrderBy(r => r.CreatedAt).ThenBy(r => r.Id)
                .Select(r => new BotReferenceDto(r.Id, ReferenceUrl(b.Id, r.Id)))
                .ToList()));
    }

    /// <summary>Where the web app loads a reference picture; only the bot's creator gets it.</summary>
    public static string ReferenceUrl(Guid botId, Guid referenceId) => $"/api/bots/{botId}/references/{referenceId}";

    public static BotDto WithModelStatus(this BotDto bot, Func<string, ModelStatus> lookup)
    {
        var status = lookup(bot.ModelId);
        return bot with { ModelStatus = status.Status, ModelStatusReason = status.Reason };
    }

    public static BotSummaryDto ToSummaryDto(this Bot bot) => new(bot.Id, bot.Name, bot.AvatarUrl, bot.IsActive, bot.ModelId);
}
