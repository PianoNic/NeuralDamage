using Mediator;
using Microsoft.EntityFrameworkCore;
using NeuralDamage.Infrastructure.Dtos;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.BotDecision;
using NeuralDamage.Infrastructure;
using NeuralDamage.Infrastructure.Mappers;
using NeuralDamage.Infrastructure.Models;

namespace NeuralDamage.Application.Queries;

public record GetBotsQuery : IQuery<Result<List<BotDto>>>;

public class GetBotsHandler(NeuralDamageDbContext db) : IQueryHandler<GetBotsQuery, Result<List<BotDto>>>
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
