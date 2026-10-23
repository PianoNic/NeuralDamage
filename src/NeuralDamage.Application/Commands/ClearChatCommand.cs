using Mediator;
using Microsoft.EntityFrameworkCore;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.BotDecision;
using NeuralDamage.Infrastructure.Services.Attachments;
using NeuralDamage.Infrastructure;
using NeuralDamage.Infrastructure.Models;
using NeuralDamage.Domain.Enums;

namespace NeuralDamage.Application.Commands;

public record ClearChatCommand(Guid ChatId, Guid RequestingUserId) : ICommand<Result>;

public class ClearChatHandler(NeuralDamageDbContext db, IChatNotificationService notifications, IAttachmentStorage attachmentStorage) : ICommandHandler<ClearChatCommand, Result>
{
    public async ValueTask<Result> Handle(ClearChatCommand request, CancellationToken cancellationToken)
    {
        var isOwner = await Admins.RunsChatAsync(db, request.ChatId, request.RequestingUserId, cancellationToken);
        if (!isOwner)
            return Result.Failure("Only the chat owner can clear messages.");

        var messages = await db.Messages.Where(m => m.ChatId == request.ChatId).ToListAsync(cancellationToken);
        db.Messages.RemoveRange(messages);
        db.Attachments.RemoveRange(await db.Attachments.Where(a => a.ChatId == request.ChatId).ToListAsync(cancellationToken));
        // The summary is of the messages that are gone.
        if (await db.Chats.FirstOrDefaultAsync(c => c.Id == request.ChatId, cancellationToken) is { } chat)
        {
            chat.Summary = null;
            chat.SummarizedBefore = null;
        }
        await db.SaveChangesAsync(cancellationToken);
        attachmentStorage.DeleteChat(request.ChatId);

        await notifications.NotifyChatCleared(request.ChatId);
        return Result.Success();
    }
}
