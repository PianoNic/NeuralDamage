using Mediator;
using Microsoft.EntityFrameworkCore;
using NeuralDamage.Application.Dtos;
using NeuralDamage.Application.Interfaces;
using NeuralDamage.Application.Mappers;
using NeuralDamage.Application.Models;

namespace NeuralDamage.Application.Queries;

public record GetBotQuery(Guid BotId) : IQuery<Result<BotDto>>;

public class GetBotHandler(INeuralDamageDbContext db) : IQueryHandler<GetBotQuery, Result<BotDto>>
{
    public async ValueTask<Result<BotDto>> Handle(GetBotQuery request, CancellationToken cancellationToken)
    {
        var bot = await db.Bots.AsNoTracking().FirstOrDefaultAsync(b => b.Id == request.BotId, cancellationToken);
        if (bot is null)
            return Result<BotDto>.Failure("Bot not found.");

        return Result<BotDto>.Success(bot.ToDto());
    }
}
