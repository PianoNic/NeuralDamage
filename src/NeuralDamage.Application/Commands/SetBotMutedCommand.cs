using Mediator;
using Microsoft.EntityFrameworkCore;
using NeuralDamage.Infrastructure;
using NeuralDamage.Infrastructure.Models;
using NeuralDamage.Infrastructure.Services;

namespace NeuralDamage.Application.Commands;

/// <summary>What /mute and /unmute do, for the profile card's button. Any member may use it.</summary>
public record SetBotMutedCommand(Guid ChatId, Guid BotId, Guid RequestingUserId, bool Muted) : ICommand<Result>;

public class SetBotMutedHandler(NeuralDamageDbContext db, IChatBotState botState, IChatNotificationService notifications) : ICommandHandler<SetBotMutedCommand, Result>
{
    public async ValueTask<Result> Handle(SetBotMutedCommand request, CancellationToken cancellationToken)
    {
        var userName = await db.ChatMembers
            .Where(cm => cm.ChatId == request.ChatId && cm.UserId == request.RequestingUserId)
            .Select(cm => cm.User!.DisplayName)
            .FirstOrDefaultAsync(cancellationToken);
        if (userName is null)
            return Result.Failure("You are not a member of this chat.");

        var botName = await db.ChatMembers
            .Where(cm => cm.ChatId == request.ChatId && cm.BotId == request.BotId)
            .Select(cm => cm.Bot!.Name)
            .FirstOrDefaultAsync(cancellationToken);
        if (botName is null)
            return Result.Failure("Bot is not in this chat.");

        if (request.Muted)
            botState.Mute(request.ChatId, request.BotId);
        else
            botState.Unmute(request.ChatId, request.BotId);

        if (userName.Length == 0)
            userName = "Someone";
        await notifications.NotifySystemMessage(request.ChatId, $"{userName} {(request.Muted ? "muted" : "unmuted")} {botName}.");
        return Result.Success();
    }
}
