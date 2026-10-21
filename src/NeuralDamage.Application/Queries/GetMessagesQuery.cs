using Mediator;
using Microsoft.EntityFrameworkCore;
using NeuralDamage.Infrastructure.Dtos;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.BotDecision;
using NeuralDamage.Infrastructure;
using NeuralDamage.Infrastructure.Mappers;
using NeuralDamage.Infrastructure.Models;

namespace NeuralDamage.Application.Queries;

/// <summary>
/// A page of a chat's messages, in chronological order. With <see cref="Before"/> it is the newest
/// page older than that point (scrolling back); with <see cref="After"/> it is the oldest page newer
/// than it, which is how a client catches up on what it missed while disconnected.
/// </summary>
public record GetMessagesQuery(Guid ChatId, Guid RequestingUserId, int Limit = 50, DateTime? Before = null, DateTime? After = null) : IQuery<Result<List<MessageDto>>>;

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
        if (request.After is not null)
            query = query.Where(m => m.CreatedAt > request.After);

        var limit = Math.Clamp(request.Limit, 1, 100);

        // Catching up reads forward from the cursor, so a gap longer than one page is filled oldest first.
        var ordered = request.After is not null
            ? query.OrderBy(m => m.CreatedAt)
            : query.OrderByDescending(m => m.CreatedAt);

        var messages = await ordered
            .Take(limit)
            .Include(m => m.SenderUser)
            .Include(m => m.SenderBot)
            .Include(m => m.Reactions).ThenInclude(r => r.User)
            .Include(m => m.Reactions).ThenInclude(r => r.Bot)
            .Include(m => m.Attachments)
            .Include(m => m.Sticker)
            // Reactions and attachments are both collections; one joined query multiplies them.
            .AsSplitQuery()
            .Include(m => m.ReplyTo!).ThenInclude(r => r.SenderUser)
            .Include(m => m.ReplyTo!).ThenInclude(r => r.SenderBot)
            .ToListAsync(cancellationToken);

        // Return in chronological order
        if (request.After is null)
            messages.Reverse();

        return Result<List<MessageDto>>.Success(messages.Select(m => m.ToDto()).ToList());
    }
}
