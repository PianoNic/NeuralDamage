using Mediator;
using Microsoft.EntityFrameworkCore;
using NeuralDamage.Application.Dtos;
using NeuralDamage.Application.Interfaces;
using NeuralDamage.Application.Mappers;
using NeuralDamage.Application.Models;

namespace NeuralDamage.Application.Queries;

public record GetChatQuery(Guid ChatId, Guid RequestingUserId) : IQuery<Result<ChatDetailDto>>;

public class GetChatHandler(INeuralDamageDbContext db) : IQueryHandler<GetChatQuery, Result<ChatDetailDto>>
{
    public async ValueTask<Result<ChatDetailDto>> Handle(GetChatQuery request, CancellationToken cancellationToken)
    {
        var isMember = await db.ChatMembers.AnyAsync(cm => cm.ChatId == request.ChatId && cm.UserId == request.RequestingUserId, cancellationToken);
        if (!isMember)
            return Result<ChatDetailDto>.Failure("You are not a member of this chat.");

        var chat = await db.Chats.AsNoTracking().FirstOrDefaultAsync(c => c.Id == request.ChatId, cancellationToken);
        if (chat is null)
            return Result<ChatDetailDto>.Failure("Chat not found.");

        var members = await db.ChatMembers
            .Where(cm => cm.ChatId == request.ChatId)
            .Include(cm => cm.User)
            .Include(cm => cm.Bot)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return Result<ChatDetailDto>.Success(chat.ToDetailDto(members.Select(m => m.ToDto()).ToList()));
    }
}
