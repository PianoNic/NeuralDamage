using Mediator;
using Microsoft.EntityFrameworkCore;
using NeuralDamage.Infrastructure.Dtos;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.BotDecision;
using NeuralDamage.Infrastructure;
using NeuralDamage.Infrastructure.Mappers;
using NeuralDamage.Infrastructure.Models;

namespace NeuralDamage.Application.Queries;

public record GetMessagesQuery(Guid ChatId, Guid RequestingUserId, int Limit = 50, DateTime? Before = null) : IQuery<Result<List<MessageDto>>>;

public class GetMessagesHandler(NeuralDamageDbContext db) : IQueryHandler<GetMessagesQuery, Result<List<MessageDto>>>
{
    public async ValueTask<Result<List<MessageDto>>> Handle(GetMessagesQuery request, CancellationToken cancellationToken)
    {
        var isMember = await db.ChatMembers.AnyAsync(cm => cm.ChatId == request.ChatId && cm.UserId == request.RequestingUserId, cancellationToken);
        if (!isMember)
            return Result<List<MessageDto>>.Failure("You are not a member of this chat.");

        var query = db.Messages
            .Where(m => m.ChatId == request.ChatId)
            .AsNoTracking();

        if (request.Before is not null)
            query = query.Where(m => m.CreatedAt < request.Before);

        var limit = Math.Clamp(request.Limit, 1, 100);

        var messages = await query
            .OrderByDescending(m => m.CreatedAt)
            .Take(limit)
            .Include(m => m.SenderUser)
            .Include(m => m.SenderBot)
            .ToListAsync(cancellationToken);

        // Return in chronological order
        messages.Reverse();

        return Result<List<MessageDto>>.Success(messages.Select(m => m.ToDto()).ToList());
    }
}
