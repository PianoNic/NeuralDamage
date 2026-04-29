using Mediator;
using Microsoft.EntityFrameworkCore;
using NeuralDamage.Application.Interfaces;
using NeuralDamage.Application.Models;
using NeuralDamage.Domain.Enums;

namespace NeuralDamage.Application.Commands;

public record DeleteChatCommand(Guid ChatId, Guid RequestingUserId) : ICommand<Result>;

public class DeleteChatHandler(INeuralDamageDbContext db, IChatNotificationService notifications) : ICommandHandler<DeleteChatCommand, Result>
{
    public async ValueTask<Result> Handle(DeleteChatCommand request, CancellationToken cancellationToken)
    {
        var chat = await db.Chats.FirstOrDefaultAsync(c => c.Id == request.ChatId, cancellationToken);
        if (chat is null)
            return Result.Failure("Chat not found.");

        var isOwner = await db.ChatMembers.AnyAsync(cm => cm.ChatId == request.ChatId && cm.UserId == request.RequestingUserId && cm.Role == ChatMemberRole.Owner, cancellationToken);
        if (!isOwner)
            return Result.Failure("Only the chat owner can delete the chat.");

        db.Chats.Remove(chat);
        await db.SaveChangesAsync(cancellationToken);

        await notifications.NotifyChatDeleted(request.ChatId);
        return Result.Success();
    }
}
