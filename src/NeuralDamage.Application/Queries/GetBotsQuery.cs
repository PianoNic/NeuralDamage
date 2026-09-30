using Mediator;
using Microsoft.EntityFrameworkCore;
using NeuralDamage.Infrastructure.Dtos;
using NeuralDamage.Infrastructure;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Mappers;
using NeuralDamage.Infrastructure.Models;

namespace NeuralDamage.Application.Queries;

/// <summary>The active public bots, or only those <paramref name="CreatedById"/> made when set.</summary>
/// <remarks>Private bots never appear here; they are only seen in their chat's member list.</remarks>
public record GetBotsQuery(Guid? CreatedById = null) : IQuery<Result<List<BotDto>>>;

public class GetBotsHandler(NeuralDamageDbContext db, IOpenRouterService openRouter, ModelPolicy modelPolicy) : IQueryHandler<GetBotsQuery, Result<List<BotDto>>>
{
    public async ValueTask<Result<List<BotDto>>> Handle(GetBotsQuery request, CancellationToken cancellationToken)
    {
        var query = db.Bots.Where(b => b.IsActive && b.IsPublic);
        if (request.CreatedById is { } creator)
            query = query.Where(b => b.CreatedById == creator);

        var bots = await query
            .OrderBy(b => b.Name)
            .SelectDto(DateTime.UtcNow)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var modelLookup = await modelPolicy.StatusLookupAsync(openRouter, cancellationToken);
        return Result<List<BotDto>>.Success(bots.Select(b => b.WithModelStatus(modelLookup)).ToList());
    }
}
