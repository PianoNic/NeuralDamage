using Mediator;
using Microsoft.EntityFrameworkCore;
using NeuralDamage.Application.Interfaces;
using NeuralDamage.Application.Models;
using NeuralDamage.Domain.Enums;

namespace NeuralDamage.Application.Command;

public record RemoveMemberCommand(Guid ChatId, Guid MemberId, Guid RequestingUserId) : ICommand<Result>;

public class RemoveMemberHandler(INeuralDamageDbContext db, IChatNotificationService notifications) : ICommandHandler<RemoveMemberCommand, Result>
{
    public async ValueTask<Result> Handle(RemoveMemberCommand request, CancellationToken cancellationToken)
    {
        var member = await db.ChatMembers.FirstOrDefaultAsync(cm => cm.Id == request.MemberId && cm.ChatId == request.ChatId, cancellationToken);
        if (member is null)
            return Result.Failure("Member not found.");

        // Allow self-removal or owner removal
        var isSelf = member.UserId == request.RequestingUserId;
        var isOwner = await db.ChatMembers.AnyAsync(cm => cm.ChatId == request.ChatId && cm.UserId == request.RequestingUserId && cm.Role == ChatMemberRole.Owner, cancellationToken);

        if (!isSelf && !isOwner)
            return Result.Failure("Only the chat owner or the member themselves can remove a member.");

        // Cannot remove the owner
        if (member.Role == ChatMemberRole.Owner)
            return Result.Failure("Cannot remove the chat owner.");

        var removedUserId = member.UserId;
        db.ChatMembers.Remove(member);
        await db.SaveChangesAsync(cancellationToken);

        await notifications.NotifyMemberRemoved(request.ChatId, request.MemberId);

        if (removedUserId is not null)
            await notifications.NotifyUserChatLeft(removedUserId.Value, request.ChatId);

        return Result.Success();
    }
}
