using Mediator;
using Microsoft.EntityFrameworkCore;
using NeuralDamage.Domain;
using NeuralDamage.Domain.Enums;
using NeuralDamage.Infrastructure;
using NeuralDamage.Infrastructure.Dtos;
using NeuralDamage.Infrastructure.Mappers;
using NeuralDamage.Infrastructure.Models;
using NeuralDamage.Infrastructure.Services;

namespace NeuralDamage.Application.Commands;

/// <summary>
/// The direct chat between a person and a bot or another person: the one there is, or a new one.
/// There is only ever one per pair, so messaging a bot twice lands in the same chat.
/// </summary>
public record OpenDirectChatCommand(Guid UserId, Guid? BotId, Guid? OtherUserId) : ICommand<Result<ChatDto>>;

public class OpenDirectChatHandler(NeuralDamageDbContext db, IChatNotificationService notifications) : ICommandHandler<OpenDirectChatCommand, Result<ChatDto>>
{
    public async ValueTask<Result<ChatDto>> Handle(OpenDirectChatCommand request, CancellationToken cancellationToken)
    {
        if ((request.BotId is null) == (request.OtherUserId is null))
            return Result.Failure<ChatDto>("Pick one bot or one person.");

        var me = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == request.UserId, cancellationToken);
        if (me is null)
            return Result.Failure<ChatDto>("User not found.");

        string name;
        Guid otherId;
        if (request.BotId is { } botId)
        {
            var bot = await db.Bots.AsNoTracking().FirstOrDefaultAsync(b => b.Id == botId && b.IsActive, cancellationToken);
            if (bot is null)
                return Result.Failure<ChatDto>("Bot not found or inactive.");
            if (!bot.IsPublic)
                return Result.Failure<ChatDto>("This bot is private to another chat.");
            name = bot.Name;
            otherId = bot.Id;
        }
        else
        {
            otherId = request.OtherUserId!.Value;
            if (otherId == me.Id)
                return Result.Failure<ChatDto>("That is you.");
            var other = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == otherId, cancellationToken);
            if (other is null)
                return Result.Failure<ChatDto>("User not found.");
            name = $"{NameOf(me)} & {NameOf(other)}";
        }

        var chat = await DirectChats.OpenAsync(db, notifications, me, otherId, otherIsBot: request.BotId is not null, name, cancellationToken);
        return chat is null ? Result.Failure<ChatDto>("Could not open the chat.") : Result.Success(chat.ToDto());
    }

    private static string NameOf(User user) => user.DisplayName != "" ? user.DisplayName : user.Email;
}

public static class DirectChatRules
{
    public const string KeepsItsPair = "A direct chat stays between the two of you. Start a new chat to bring in more.";
}
