using Mediator;
using Microsoft.EntityFrameworkCore;
using NeuralDamage.Application.Interfaces;
using NeuralDamage.Application.Mappers;
using NeuralDamage.Application.Models;
using NeuralDamage.Domain;
using NeuralDamage.Domain.Enums;

namespace NeuralDamage.Application.Commands;

public record AddMemberCommand(Guid ChatId, Guid? UserId, Guid? BotId, Guid RequestingUserId) : ICommand<Result>;

public class AddMemberHandler(INeuralDamageDbContext db, IChatNotificationService notifications) : ICommandHandler<AddMemberCommand, Result>
{
    public async ValueTask<Result> Handle(AddMemberCommand request, CancellationToken cancellationToken)
    {
        if (request.UserId is null && request.BotId is null)
            return Result.Failure("Either UserId or BotId must be provided.");
        if (request.UserId is not null && request.BotId is not null)
            return Result.Failure("Only one of UserId or BotId can be provided.");

        var chatExists = await db.Chats.AnyAsync(c => c.Id == request.ChatId, cancellationToken);
        if (!chatExists)
            return Result.Failure("Chat not found.");

        var isOwner = await db.ChatMembers.AnyAsync(cm => cm.ChatId == request.ChatId && cm.UserId == request.RequestingUserId && cm.Role == ChatMemberRole.Owner, cancellationToken);
        if (!isOwner)
            return Result.Failure("Only the chat owner can add members.");

        if (request.UserId is not null)
        {
            var userExists = await db.Users.AnyAsync(u => u.Id == request.UserId, cancellationToken);
            if (!userExists) return Result.Failure("User not found.");

            var alreadyMember = await db.ChatMembers.AnyAsync(cm => cm.ChatId == request.ChatId && cm.UserId == request.UserId, cancellationToken);
            if (alreadyMember) return Result.Failure("User is already a member.");
        }

        if (request.BotId is not null)
        {
            var botExists = await db.Bots.AnyAsync(b => b.Id == request.BotId && b.IsActive, cancellationToken);
            if (!botExists) return Result.Failure("Bot not found or inactive.");

            var alreadyMember = await db.ChatMembers.AnyAsync(cm => cm.ChatId == request.ChatId && cm.BotId == request.BotId, cancellationToken);
            if (alreadyMember) return Result.Failure("Bot is already a member.");
        }

        var member = new ChatMember
        {
            ChatId = request.ChatId,
            UserId = request.UserId,
            BotId = request.BotId,
            Role = request.BotId is not null ? ChatMemberRole.Member : ChatMemberRole.Member
        };
        db.ChatMembers.Add(member);
        await db.SaveChangesAsync(cancellationToken);

        // Reload with navigation properties for DTO
        var loaded = await db.ChatMembers
            .Include(cm => cm.User)
            .Include(cm => cm.Bot)
            .FirstAsync(cm => cm.Id == member.Id, cancellationToken);

        await notifications.NotifyMemberAdded(request.ChatId, loaded.ToDto());

        // Notify the added user so they can join the chat in their UI
        if (request.UserId is not null)
        {
            var chat = await db.Chats.AsNoTracking().FirstAsync(c => c.Id == request.ChatId, cancellationToken);
            var allMembers = await db.ChatMembers
                .Where(cm => cm.ChatId == request.ChatId)
                .Include(cm => cm.User).Include(cm => cm.Bot)
                .AsNoTracking().ToListAsync(cancellationToken);
            await notifications.NotifyUserChatJoined(request.UserId.Value, chat.ToDetailDto(allMembers.Select(m => m.ToDto()).ToList()));
        }

        return Result.Success();
    }
}
