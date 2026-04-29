using Mediator;
using Microsoft.EntityFrameworkCore;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.BotDecision;
using NeuralDamage.Infrastructure;
using NeuralDamage.Infrastructure.Models;
using NeuralDamage.Domain.Enums;

namespace NeuralDamage.Application.Commands;

public record ClearChatCommand(Guid ChatId, Guid RequestingUserId) : ICommand<Result>;

public class ClearChatHandler(NeuralDamageDbContext db, IChatNotificationService notifications) : ICommandHandler<ClearChatCommand, Result>
{
    public async ValueTask<Result> Handle(ClearChatCommand request, CancellationToken cancellationToken)
    {
        var isOwner = await db.ChatMembers.AnyAsync(cm => cm.ChatId == request.ChatId && cm.UserId == request.RequestingUserId && cm.Role == ChatMemberRole.Owner, cancellationToken);
        if (!isOwner)
            return Result.Failure("Only the chat owner can clear messages.");

        var messages = await db.Messages.Where(m => m.ChatId == request.ChatId).ToListAsync(cancellationToken);
        db.Messages.RemoveRange(messages);
        await db.SaveChangesAsync(cancellationToken);

        await notifications.NotifyChatCleared(request.ChatId);
        return Result.Success();
    }
}
