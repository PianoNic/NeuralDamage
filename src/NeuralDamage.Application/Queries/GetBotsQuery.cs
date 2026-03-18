using Mediator;
using Microsoft.EntityFrameworkCore;
using NeuralDamage.Application.Dtos;
using NeuralDamage.Application.Interfaces;
using NeuralDamage.Application.Mappers;
using NeuralDamage.Application.Models;

namespace NeuralDamage.Application.Queries;

public record GetBotsQuery : IQuery<Result<List<BotDto>>>;

public class GetBotsHandler(INeuralDamageDbContext db) : IQueryHandler<GetBotsQuery, Result<List<BotDto>>>
{
    public async ValueTask<Result<List<BotDto>>> Handle(GetBotsQuery request, CancellationToken cancellationToken)
    {
        var bots = await db.Bots
            .Where(b => b.IsActive)
            .OrderBy(b => b.Name)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return Result<List<BotDto>>.Success(bots.Select(b => b.ToDto()).ToList());
    }
}
