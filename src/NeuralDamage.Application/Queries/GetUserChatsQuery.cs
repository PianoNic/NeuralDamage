using Mediator;
using Microsoft.EntityFrameworkCore;
using NeuralDamage.Infrastructure.Dtos;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.BotDecision;
using NeuralDamage.Infrastructure;
using NeuralDamage.Infrastructure.Mappers;
using NeuralDamage.Infrastructure.Models;

namespace NeuralDamage.Application.Queries;

public record GetUserChatsQuery(Guid UserId) : IQuery<Result<List<ChatDto>>>;

public class GetUserChatsHandler(NeuralDamageDbContext db) : IQueryHandler<GetUserChatsQuery, Result<List<ChatDto>>>
{
    /// <summary>The last message is cut to this for the inbox line.</summary>
    private const int PreviewLength = 120;

    public async ValueTask<Result<List<ChatDto>>> Handle(GetUserChatsQuery request, CancellationToken cancellationToken)
    {
        var rows = await db.ChatMembers
            .Where(cm => cm.UserId == request.UserId)
            .Select(cm => cm.Chat)
            .OrderByDescending(c => c.UpdatedAt)
            .Select(c => new
            {
                c.Id,
                c.Name,
                c.CreatedById,
                c.CreatedAt,
                c.UpdatedAt,
                IsDirect = c.DirectKey != null,
                c.AvatarUrl,
                MemberCount = c.Members.Count,
                Last = c.Messages
                    .OrderByDescending(m => m.CreatedAt)
                    .Select(m => new
                    {
                        m.Content,
                        HasSticker = m.StickerId != null,
                        HasImage = m.Attachments.Any(),
                        Sender = m.SenderBot != null ? m.SenderBot.Name : m.SenderUser != null ? m.SenderUser.DisplayName : null,
                        Mine = m.SenderUserId == request.UserId,
                        m.CreatedAt,
                    })
                    .FirstOrDefault(),
                Members = c.Members
                    .Where(cm => cm.UserId != request.UserId)
                    .OrderBy(cm => cm.BotId == null)
                    .ThenBy(cm => cm.JoinedAt)
                    .Take(2)
                    .Select(cm => new ChatPreviewMemberDto(
                        cm.Bot != null ? cm.Bot.Name : cm.User!.DisplayName,
                        cm.Bot != null ? cm.Bot.AvatarUrl : cm.User!.AvatarUrl,
                        cm.Bot != null ? cm.Bot.ModelId : null,
                        cm.BotId != null))
                    .ToList(),
            })
            .ToListAsync(cancellationToken);

        var chats = rows
            .Select(r => new ChatDto(
                r.Id,
                r.Name,
                r.CreatedById,
                r.CreatedAt,
                r.UpdatedAt,
                r.IsDirect,
                new ChatPreviewDto(
                    r.Last is null ? null : PreviewOf(r.Last.Content, r.Last.HasSticker, r.Last.HasImage),
                    r.Last is null ? null : r.Last.Mine ? "You" : r.Last.Sender,
                    r.Last?.CreatedAt,
                    r.MemberCount,
                    r.Members),
                r.AvatarUrl))
            .ToList();

        return Result<List<ChatDto>>.Success(chats);
    }

    private static string PreviewOf(string content, bool hasSticker, bool hasImage)
    {
        var text = content.ReplaceLineEndings(" ").Trim();
        if (text.Length == 0) return hasSticker ? "Sent a sticker" : hasImage ? "Sent a picture" : "";
        return text.Length <= PreviewLength ? text : text[..PreviewLength].TrimEnd() + "…";
    }
}
