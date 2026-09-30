using Mediator;
using Microsoft.EntityFrameworkCore;
using NeuralDamage.Infrastructure.Dtos;
using NeuralDamage.Infrastructure;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Mappers;
using NeuralDamage.Infrastructure.Models;

namespace NeuralDamage.Application.Queries;

/// <remarks>A private bot is only visible to its creator and the members of its chat.</remarks>
public record GetBotQuery(Guid BotId, Guid RequestingUserId) : IQuery<Result<BotDto>>;

public class GetBotHandler(NeuralDamageDbContext db, IOpenRouterService openRouter, ModelPolicy modelPolicy) : IQueryHandler<GetBotQuery, Result<BotDto>>
{
    public async ValueTask<Result<BotDto>> Handle(GetBotQuery request, CancellationToken cancellationToken)
    {
        var bot = await db.Bots
            .Where(b => b.Id == request.BotId)
            .Where(b => b.IsPublic
                || b.CreatedById == request.RequestingUserId
                || db.ChatMembers.Any(cm => cm.ChatId == b.ChatId && cm.UserId == request.RequestingUserId))
            .SelectDto(DateTime.UtcNow)
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);

        if (bot is null)
            return Result<BotDto>.Failure("Bot not found.");

        var modelLookup = await modelPolicy.StatusLookupAsync(openRouter, cancellationToken);
        return Result<BotDto>.Success(bot.WithModelStatus(modelLookup));
    }
}
