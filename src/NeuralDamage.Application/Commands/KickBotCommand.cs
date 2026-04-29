using Mediator;
using Microsoft.EntityFrameworkCore;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.BotDecision;
using NeuralDamage.Infrastructure;
using NeuralDamage.Infrastructure.Models;
using NeuralDamage.Domain.Enums;

namespace NeuralDamage.Application.Commands;

public record KickBotCommand(Guid ChatId, Guid BotId, Guid RequestingUserId) : ICommand<Result>;

public class KickBotHandler(NeuralDamageDbContext db, IChatNotificationService notifications) : ICommandHandler<KickBotCommand, Result>
{
    public async ValueTask<Result> Handle(KickBotCommand request, CancellationToken cancellationToken)
    {
        var isOwner = await db.ChatMembers.AnyAsync(cm => cm.ChatId == request.ChatId && cm.UserId == request.RequestingUserId && cm.Role == ChatMemberRole.Owner, cancellationToken);
        if (!isOwner)
            return Result.Failure("Only the chat owner can kick bots.");

        var member = await db.ChatMembers.FirstOrDefaultAsync(cm => cm.ChatId == request.ChatId && cm.BotId == request.BotId, cancellationToken);
        if (member is null)
            return Result.Failure("Bot is not in this chat.");

        db.ChatMembers.Remove(member);
        await db.SaveChangesAsync(cancellationToken);

        await notifications.NotifyMemberRemoved(request.ChatId, member.Id);
        return Result.Success();
    }
}
