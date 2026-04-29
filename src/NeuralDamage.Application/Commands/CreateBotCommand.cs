using Mediator;
using NeuralDamage.Application.Dtos;
using NeuralDamage.Application.Interfaces;
using NeuralDamage.Application.Mappers;
using NeuralDamage.Application.Models;
using NeuralDamage.Domain;

namespace NeuralDamage.Application.Commands;

public record CreateBotCommand(string Name, string ModelId, string SystemPrompt, string? Personality, double Temperature, string? AvatarUrl, string? Aliases, Guid CreatedById) : ICommand<Result<BotDto>>;

public class CreateBotHandler(INeuralDamageDbContext db) : ICommandHandler<CreateBotCommand, Result<BotDto>>
{
    public async ValueTask<Result<BotDto>> Handle(CreateBotCommand request, CancellationToken cancellationToken)
    {
        var bot = new Bot
        {
            Name = request.Name,
            ModelId = request.ModelId,
            SystemPrompt = request.SystemPrompt,
            Personality = request.Personality,
            Temperature = request.Temperature,
            AvatarUrl = request.AvatarUrl,
            Aliases = request.Aliases,
            CreatedById = request.CreatedById
        };

        db.Bots.Add(bot);
        await db.SaveChangesAsync(cancellationToken);

        return Result<BotDto>.Success(bot.ToDto());
    }
}
