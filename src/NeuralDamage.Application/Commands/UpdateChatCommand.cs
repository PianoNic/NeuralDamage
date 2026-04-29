using Mediator;
using Microsoft.EntityFrameworkCore;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.BotDecision;
using NeuralDamage.Infrastructure;
using NeuralDamage.Infrastructure.Mappers;
using NeuralDamage.Infrastructure.Models;
using NeuralDamage.Domain.Enums;

namespace NeuralDamage.Application.Commands;

public record UpdateChatCommand(Guid ChatId, string Name, Guid RequestingUserId) : ICommand<Result>;

public class UpdateChatHandler(NeuralDamageDbContext db, IChatNotificationService notifications) : ICommandHandler<UpdateChatCommand, Result>
{
    public async ValueTask<Result> Handle(UpdateChatCommand request, CancellationToken cancellationToken)
    {
        var chat = await db.Chats.FirstOrDefaultAsync(c => c.Id == request.ChatId, cancellationToken);
        if (chat is null)
            return Result.Failure("Chat not found.");

        var isOwner = await db.ChatMembers.AnyAsync(cm => cm.ChatId == request.ChatId && cm.UserId == request.RequestingUserId && cm.Role == ChatMemberRole.Owner, cancellationToken);
        if (!isOwner)
            return Result.Failure("Only the chat owner can update the chat.");

        chat.Name = request.Name;
        await db.SaveChangesAsync(cancellationToken);

        await notifications.NotifyChatUpdated(chat.Id, chat.ToDto());
        return Result.Success();
    }
}
