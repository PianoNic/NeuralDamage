using Mediator;
using Microsoft.EntityFrameworkCore;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.BotDecision;
using NeuralDamage.Infrastructure;
using NeuralDamage.Infrastructure.Models;
using NeuralDamage.Infrastructure.Services.Attachments;

namespace NeuralDamage.Application.Commands;

public record DeleteBotCommand(Guid BotId, Guid RequestingUserId) : ICommand<Result>;

/// <summary>
/// Retires a bot: it stops answering and leaves every chat it is in. The row
/// stays, so the messages it already sent keep their sender. Its own picture
/// goes, and those messages show its model's icon.
/// </summary>
public class DeleteBotHandler(NeuralDamageDbContext db, IChatNotificationService notifications, IAttachmentStorage storage) : ICommandHandler<DeleteBotCommand, Result>
{
    public async ValueTask<Result> Handle(DeleteBotCommand request, CancellationToken cancellationToken)
    {
        var bot = await db.Bots.FirstOrDefaultAsync(b => b.Id == request.BotId, cancellationToken);
        if (bot is null)
            return Result.Failure("Bot not found.");

        if (bot.CreatedById != request.RequestingUserId)
            return Result.Failure("Only the bot creator can delete this bot.");

        bot.IsActive = false;
        bot.AvatarUrl = null;
        storage.DeleteBotAvatar(bot.Id);
        var memberships = await db.ChatMembers.Where(cm => cm.BotId == bot.Id).ToListAsync(cancellationToken);
        db.ChatMembers.RemoveRange(memberships);
        await db.SaveChangesAsync(cancellationToken);

        foreach (var member in memberships)
            await notifications.NotifyMemberRemoved(member.ChatId, member.Id);
        return Result.Success();
    }
}
