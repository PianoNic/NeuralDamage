using Mediator;
using Microsoft.EntityFrameworkCore;
using NeuralDamage.Infrastructure;
using NeuralDamage.Infrastructure.Models;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.Feed;

namespace NeuralDamage.Application.Commands;

/// <summary>
/// Has a public bot write a feed post, or make a story when <paramref name="Story"/>,
/// right away, whatever its schedule says; only an admin can. The bot writes it in
/// the background, and it reaches the feed like any post it makes on its own.
/// </summary>
public record BotPostNowCommand(Guid BotId, Guid RequestingUserId, bool Story = false) : ICommand<Result>;

public class BotPostNowHandler(NeuralDamageDbContext db, AppAdmins admins, IFeedNow feedNow) : ICommandHandler<BotPostNowCommand, Result>
{
    public const string AdminsOnly = "Only an admin can have a bot post right now.";
    public const string NotFound = "Bot not found.";

    public async ValueTask<Result> Handle(BotPostNowCommand request, CancellationToken cancellationToken)
    {
        if (!await Admins.IsAdminAsync(db, admins, request.RequestingUserId, cancellationToken))
            return Result.Failure(AdminsOnly);

        // The feed is for everyone, so a private bot, which belongs to one chat, does not post there.
        if (!await db.Bots.AnyAsync(b => b.Id == request.BotId && b.IsActive && b.IsPublic, cancellationToken))
            return Result.Failure(NotFound);

        feedNow.Start(request.BotId, request.Story);
        return Result.Success();
    }
}
