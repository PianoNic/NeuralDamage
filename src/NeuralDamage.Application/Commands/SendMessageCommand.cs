using Mediator;
using Microsoft.EntityFrameworkCore;
using NeuralDamage.Application.Interfaces;
using NeuralDamage.Application.Mappers;
using NeuralDamage.Application.Models;
using NeuralDamage.Domain;

namespace NeuralDamage.Application.Commands;

public record SendMessageCommand(Guid ChatId, Guid SenderUserId, string Content, Guid? ReplyToId = null) : ICommand<Result>;

public class SendMessageHandler(INeuralDamageDbContext db, IChatNotificationService notifications, IBotResponseOrchestrator botOrchestrator, IBotResponseQueue botQueue) : ICommandHandler<SendMessageCommand, Result>
{
    public async ValueTask<Result> Handle(SendMessageCommand request, CancellationToken cancellationToken)
    {
        var isMember = await db.ChatMembers.AnyAsync(cm => cm.ChatId == request.ChatId && cm.UserId == request.SenderUserId, cancellationToken);
        if (!isMember)
            return Result.Failure("You are not a member of this chat.");

        if (request.ReplyToId is not null)
        {
            var replyExists = await db.Messages.AnyAsync(m => m.Id == request.ReplyToId && m.ChatId == request.ChatId, cancellationToken);
            if (!replyExists)
                return Result.Failure("Reply target message not found.");
        }

        // Cancel any pending bot responses for this chat (human interrupted)
        botOrchestrator.CancelPendingResponses(request.ChatId);

        var message = new Message
        {
            ChatId = request.ChatId,
            SenderUserId = request.SenderUserId,
            Content = request.Content,
            ReplyToId = request.ReplyToId
        };
        db.Messages.Add(message);
        await db.SaveChangesAsync(cancellationToken);

        // Reload with sender for DTO
        var loaded = await db.Messages
            .Include(m => m.SenderUser)
            .Include(m => m.SenderBot)
            .AsNoTracking()
            .FirstAsync(m => m.Id == message.Id, cancellationToken);

        await notifications.NotifyMessageNew(request.ChatId, loaded.ToDto());

        // Enqueue bot response processing (fire-and-forget via background service)
        if (!request.Content.StartsWith('/'))
            await botQueue.EnqueueAsync(request.ChatId, message.Id, cancellationToken);

        return Result.Success();
    }
}
