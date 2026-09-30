using Mediator;
using Microsoft.EntityFrameworkCore;
using NeuralDamage.Infrastructure;
using NeuralDamage.Infrastructure.Models;
using NeuralDamage.Infrastructure.Services.Attachments;

namespace NeuralDamage.Application.Commands;

/// <summary>
/// Gives a bot its own profile picture, replacing the one it had. The web app
/// crops and downscales it before upload; here it only has to be a real image.
/// Returns the bot's new avatar URL.
/// </summary>
public record SetBotAvatarCommand(Guid BotId, Guid RequestingUserId, byte[] Data) : ICommand<Result<string>>;

public class SetBotAvatarHandler(NeuralDamageDbContext db, IAttachmentStorage storage, AttachmentOptions options)
    : ICommandHandler<SetBotAvatarCommand, Result<string>>
{
    public async ValueTask<Result<string>> Handle(SetBotAvatarCommand request, CancellationToken cancellationToken)
    {
        var bot = await db.Bots.FirstOrDefaultAsync(b => b.Id == request.BotId && b.IsActive, cancellationToken);
        if (bot is null)
            return Result.Failure<string>("Bot not found.");

        if (bot.CreatedById != request.RequestingUserId)
            return Result.Failure<string>("Only the bot creator can change its picture.");

        if (request.Data.Length == 0)
            return Result.Failure<string>("The file is empty.");
        if (request.Data.Length > options.MaxBytes)
            return Result.Failure<string>($"Images can be at most {options.MaxBytes / (1024 * 1024.0):0.#} MB.");

        if (ImageInspector.Inspect(request.Data) is null)
            return Result.Failure<string>("Only PNG, JPEG, WebP and GIF images can be used.");

        await storage.SaveBotAvatarAsync(bot.Id, request.Data, cancellationToken);
        bot.AvatarUrl = BotAvatars.UrlOf(bot.Id);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success(bot.AvatarUrl);
    }
}

/// <summary>Takes a bot's own picture away, so it shows its model's icon again.</summary>
public record RemoveBotAvatarCommand(Guid BotId, Guid RequestingUserId) : ICommand<Result>;

public class RemoveBotAvatarHandler(NeuralDamageDbContext db, IAttachmentStorage storage) : ICommandHandler<RemoveBotAvatarCommand, Result>
{
    public async ValueTask<Result> Handle(RemoveBotAvatarCommand request, CancellationToken cancellationToken)
    {
        var bot = await db.Bots.FirstOrDefaultAsync(b => b.Id == request.BotId && b.IsActive, cancellationToken);
        if (bot is null)
            return Result.Failure("Bot not found.");

        if (bot.CreatedById != request.RequestingUserId)
            return Result.Failure("Only the bot creator can change its picture.");

        storage.DeleteBotAvatar(bot.Id);
        bot.AvatarUrl = null;
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

public static class BotAvatars
{
    /// <summary>
    /// Where the web app loads a bot's picture. The version changes with every
    /// upload, so the URL can be cached for good and a new picture still shows.
    /// </summary>
    public static string UrlOf(Guid botId) => $"/api/bots/{botId}/avatar?v={DateTime.UtcNow.Ticks}";
}
