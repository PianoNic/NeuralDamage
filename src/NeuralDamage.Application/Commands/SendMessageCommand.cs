using Mediator;
using Microsoft.EntityFrameworkCore;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.BotDecision;
using NeuralDamage.Infrastructure;
using NeuralDamage.Infrastructure.Mappers;
using NeuralDamage.Infrastructure.Models;
using NeuralDamage.Domain;

namespace NeuralDamage.Application.Commands;

public record SendMessageCommand(Guid ChatId, Guid SenderUserId, string Content, Guid? ReplyToId = null, IReadOnlyList<Guid>? AttachmentIds = null) : ICommand<Result>;

public class SendMessageHandler(NeuralDamageDbContext db, IChatNotificationService notifications, IBotResponseOrchestrator botOrchestrator, IBotResponseQueue botQueue, ISender sender, IChatBotState botState) : ICommandHandler<SendMessageCommand, Result>
{
    public async ValueTask<Result> Handle(SendMessageCommand request, CancellationToken cancellationToken)
    {
        var isMember = await db.ChatMembers.AnyAsync(cm => cm.ChatId == request.ChatId && cm.UserId == request.SenderUserId, cancellationToken);
        if (!isMember)
            return Result.Failure("You are not a member of this chat.");

        // Commands act on the chat instead of joining the conversation, so they are never saved
        var attachmentIds = request.AttachmentIds?.Distinct().ToList() ?? [];
        if (attachmentIds.Count == 0 && SlashCommand.Parse(request.Content) is not null)
            return await sender.Send(new RunSlashCommand(request.ChatId, request.SenderUserId, request.Content), cancellationToken);

        if (request.ReplyToId is not null)
        {
            var replyExists = await db.Messages.AnyAsync(m => m.Id == request.ReplyToId && m.ChatId == request.ChatId, cancellationToken);
            if (!replyExists)
                return Result.Failure("Reply target message not found.");
        }

        // Only the sender's own uploads to this chat, and only ones not sent yet.
        var attachments = attachmentIds.Count == 0
            ? []
            : await db.Attachments
                .Where(a => attachmentIds.Contains(a.Id) && a.ChatId == request.ChatId && a.UploaderUserId == request.SenderUserId && a.MessageId == null)
                .ToListAsync(cancellationToken);
        if (attachments.Count != attachmentIds.Count)
            return Result.Failure("An attached image was not found. Attach it again.");

        // Cancel any pending bot responses for this chat (human interrupted)
        botOrchestrator.CancelPendingResponses(request.ChatId);
        // A new human message lifts /stop (but not /mute)
        botState.Resume(request.ChatId);

        var message = new Message
        {
            ChatId = request.ChatId,
            SenderUserId = request.SenderUserId,
            Content = request.Content,
            ReplyToId = request.ReplyToId
        };
        db.Messages.Add(message);
        foreach (var attachment in attachments)
            attachment.MessageId = message.Id;
        await db.SaveChangesAsync(cancellationToken);

        // Reload with sender for DTO
        var loaded = await db.Messages
            .Include(m => m.SenderUser)
            .Include(m => m.SenderBot)
            .Include(m => m.Reactions).ThenInclude(r => r.User)
            .Include(m => m.Reactions).ThenInclude(r => r.Bot)
            .Include(m => m.Attachments)
            .Include(m => m.ReplyTo!).ThenInclude(r => r.SenderUser)
            .Include(m => m.ReplyTo!).ThenInclude(r => r.SenderBot)
            .AsNoTracking()
            .FirstAsync(m => m.Id == message.Id, cancellationToken);

        await notifications.NotifyMessageNew(request.ChatId, loaded.ToDto());

        // Enqueue bot response processing (fire-and-forget via background service)
        await botQueue.EnqueueAsync(request.ChatId, message.Id, cancellationToken);

        return Result.Success();
    }
}
