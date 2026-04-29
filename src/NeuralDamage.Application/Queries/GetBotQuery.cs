using Mediator;
using Microsoft.EntityFrameworkCore;
using NeuralDamage.Infrastructure.Dtos;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.BotDecision;
using NeuralDamage.Infrastructure;
using NeuralDamage.Infrastructure.Mappers;
using NeuralDamage.Infrastructure.Models;

namespace NeuralDamage.Application.Queries;

public record GetBotQuery(Guid BotId) : IQuery<Result<BotDto>>;

public class GetBotHandler(NeuralDamageDbContext db) : IQueryHandler<GetBotQuery, Result<BotDto>>
{
    public async ValueTask<Result<BotDto>> Handle(GetBotQuery request, CancellationToken cancellationToken)
    {
        var bot = await db.Bots.AsNoTracking().FirstOrDefaultAsync(b => b.Id == request.BotId, cancellationToken);
        if (bot is null)
            return Result<BotDto>.Failure("Bot not found.");

        return Result<BotDto>.Success(bot.ToDto());
    }
}
