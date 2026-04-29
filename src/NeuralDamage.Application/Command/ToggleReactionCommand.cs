using Mediator;
using Microsoft.EntityFrameworkCore;
using NeuralDamage.Application.Interfaces;
using NeuralDamage.Application.Mappers;
using NeuralDamage.Application.Models;
using NeuralDamage.Domain;

namespace NeuralDamage.Application.Command;

public record ToggleReactionCommand(Guid ChatId, Guid MessageId, string Emoji, Guid UserId) : ICommand<Result>;

public class ToggleReactionHandler(INeuralDamageDbContext db, IChatNotificationService notifications) : ICommandHandler<ToggleReactionCommand, Result>
{
    public async ValueTask<Result> Handle(ToggleReactionCommand request, CancellationToken cancellationToken)
    {
        var message = await db.Messages.AnyAsync(m => m.Id == request.MessageId && m.ChatId == request.ChatId, cancellationToken);
        if (!message)
            return Result.Failure("Message not found.");

        var existing = await db.Reactions.FirstOrDefaultAsync(r => r.MessageId == request.MessageId && r.UserId == request.UserId && r.Emoji == request.Emoji, cancellationToken);

        if (existing is not null)
        {
            db.Reactions.Remove(existing);
        }
        else
        {
            db.Reactions.Add(new Reaction { MessageId = request.MessageId, UserId = request.UserId, Emoji = request.Emoji });
        }

        await db.SaveChangesAsync(cancellationToken);

        var reactions = await db.Reactions
            .Where(r => r.MessageId == request.MessageId)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        await notifications.NotifyReactionUpdated(request.ChatId, request.MessageId, reactions.Select(r => r.ToDto()).ToList());
        return Result.Success();
    }
}
