using NeuralDamage.Application.Dtos;
using NeuralDamage.Domain;

namespace NeuralDamage.Application.Mappers;

public static class BotMapper
{
    public static BotDto ToDto(this Bot bot) => new(bot.Id, bot.Name, bot.ModelId, bot.SystemPrompt, bot.Personality, bot.Temperature, bot.AvatarUrl, bot.Aliases, bot.CreatedById, bot.IsActive, bot.CreatedAt);

    public static BotSummaryDto ToSummaryDto(this Bot bot) => new(bot.Id, bot.Name, bot.AvatarUrl, bot.IsActive);
}
