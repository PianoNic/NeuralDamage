using Mediator;
using Microsoft.EntityFrameworkCore;
using NeuralDamage.Application.Dtos;
using NeuralDamage.Application.Interfaces;
using NeuralDamage.Application.Mappers;
using NeuralDamage.Application.Models;

namespace NeuralDamage.Application.Queries;

public record GetUserChatsQuery(Guid UserId) : IQuery<Result<List<ChatDto>>>;

public class GetUserChatsHandler(INeuralDamageDbContext db) : IQueryHandler<GetUserChatsQuery, Result<List<ChatDto>>>
{
    public async ValueTask<Result<List<ChatDto>>> Handle(GetUserChatsQuery request, CancellationToken cancellationToken)
    {
        var chats = await db.ChatMembers
            .Where(cm => cm.UserId == request.UserId)
            .Select(cm => cm.Chat)
            .OrderByDescending(c => c.UpdatedAt)
            .Select(c => new ChatDto(c.Id, c.Name, c.CreatedById, c.CreatedAt, c.UpdatedAt))
            .ToListAsync(cancellationToken);

        return Result<List<ChatDto>>.Success(chats);
    }
}
