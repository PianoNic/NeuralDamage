using Mediator;
using Microsoft.EntityFrameworkCore;
using NeuralDamage.Application.Interfaces;
using NeuralDamage.Application.Models;

namespace NeuralDamage.Application.Commands;

public record DeleteBotCommand(Guid BotId, Guid RequestingUserId) : ICommand<Result>;

public class DeleteBotHandler(INeuralDamageDbContext db) : ICommandHandler<DeleteBotCommand, Result>
{
    public async ValueTask<Result> Handle(DeleteBotCommand request, CancellationToken cancellationToken)
    {
        var bot = await db.Bots.FirstOrDefaultAsync(b => b.Id == request.BotId, cancellationToken);
        if (bot is null)
            return Result.Failure("Bot not found.");

        if (bot.CreatedById != request.RequestingUserId)
            return Result.Failure("Only the bot creator can delete this bot.");

        bot.IsActive = false;
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
