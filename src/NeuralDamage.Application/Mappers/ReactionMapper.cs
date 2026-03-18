using NeuralDamage.Application.Dtos;
using NeuralDamage.Domain;

namespace NeuralDamage.Application.Mappers;

public static class ReactionMapper
{
    public static ReactionDto ToDto(this Reaction reaction) => new(reaction.Id, reaction.MessageId, reaction.UserId, reaction.BotId, reaction.Emoji, reaction.CreatedAt);
}
