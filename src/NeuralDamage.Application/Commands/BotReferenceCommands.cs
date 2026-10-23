using Mediator;
using Microsoft.EntityFrameworkCore;
using NeuralDamage.Domain;
using NeuralDamage.Infrastructure;
using NeuralDamage.Infrastructure.Dtos;
using NeuralDamage.Infrastructure.Mappers;
using NeuralDamage.Infrastructure.Models;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.Attachments;

namespace NeuralDamage.Application.Commands;

/// <summary>
/// Adds a reference picture to a bot, at most <see cref="BotReference.MaxPerBot"/>,
/// then has the describer write its <see cref="Bot.Looks"/> again from all of
/// them. Only its creator can. When the describer fails, the looks stay as they were.
/// </summary>
public record AddBotReferenceCommand(Guid BotId, Guid RequestingUserId, byte[] Data) : ICommand<Result<BotReferencesDto>>;

public class AddBotReferenceHandler(NeuralDamageDbContext db, IAttachmentStorage storage, AttachmentOptions options, ILooksDescriber describer)
    : ICommandHandler<AddBotReferenceCommand, Result<BotReferencesDto>>
{
    public async ValueTask<Result<BotReferencesDto>> Handle(AddBotReferenceCommand request, CancellationToken cancellationToken)
    {
        var bot = await db.Bots.Include(b => b.References)
            .FirstOrDefaultAsync(b => b.Id == request.BotId && b.IsActive, cancellationToken);
        if (bot is null)
            return Result.Failure<BotReferencesDto>("Bot not found.");
        if (!await Admins.RunsBotAsync(db, bot, request.RequestingUserId, cancellationToken))
            return Result.Failure<BotReferencesDto>("Only the bot creator can change its reference pictures.");
        if (bot.References.Count >= BotReference.MaxPerBot)
            return Result.Failure<BotReferencesDto>($"A bot can have at most {BotReference.MaxPerBot} reference pictures.");

        if (request.Data.Length == 0)
            return Result.Failure<BotReferencesDto>("The file is empty.");
        if (request.Data.Length > options.MaxBytes)
            return Result.Failure<BotReferencesDto>($"Images can be at most {options.MaxBytes / (1024 * 1024.0):0.#} MB.");
        if (ImageInspector.Inspect(request.Data) is not { } image)
            return Result.Failure<BotReferencesDto>("Only PNG, JPEG, WebP and GIF images can be used.");

        var reference = new BotReference { BotId = bot.Id, ContentType = image.ContentType, SizeBytes = request.Data.Length };
        await storage.SaveBotReferenceAsync(bot.Id, reference.Id, request.Data, cancellationToken);
        db.BotReferences.Add(reference);
        await db.SaveChangesAsync(cancellationToken);

        var pictures = new List<ImagePart>();
        foreach (var r in bot.References.OrderBy(r => r.CreatedAt).ThenBy(r => r.Id))
            if (await storage.ReadBotReferenceAsync(bot.Id, r.Id, cancellationToken) is { } data)
                pictures.Add(new ImagePart(r.ContentType, data));
        if (await describer.DescribeLooksAsync(pictures, cancellationToken) is { } looks)
        {
            bot.Looks = looks;
            await db.SaveChangesAsync(cancellationToken);
        }

        return Result.Success(BotReferences.ToDto(bot));
    }
}

/// <summary>Takes a reference picture off a bot. Its looks stay; the creator edits them by hand.</summary>
public record RemoveBotReferenceCommand(Guid BotId, Guid RequestingUserId, Guid ReferenceId) : ICommand<Result>;

public class RemoveBotReferenceHandler(NeuralDamageDbContext db, IAttachmentStorage storage) : ICommandHandler<RemoveBotReferenceCommand, Result>
{
    public async ValueTask<Result> Handle(RemoveBotReferenceCommand request, CancellationToken cancellationToken)
    {
        var bot = await db.Bots.FirstOrDefaultAsync(b => b.Id == request.BotId && b.IsActive, cancellationToken);
        if (bot is null)
            return Result.Failure("Bot not found.");
        if (!await Admins.RunsBotAsync(db, bot, request.RequestingUserId, cancellationToken))
            return Result.Failure("Only the bot creator can change its reference pictures.");

        var reference = await db.BotReferences.FirstOrDefaultAsync(r => r.Id == request.ReferenceId && r.BotId == bot.Id, cancellationToken);
        if (reference is null)
            return Result.Failure("Picture not found.");

        db.BotReferences.Remove(reference);
        await db.SaveChangesAsync(cancellationToken);
        storage.DeleteBotReference(bot.Id, reference.Id);
        return Result.Success();
    }
}

public static class BotReferences
{
    public static BotReferencesDto ToDto(Bot bot) => new(
        bot.References.OrderBy(r => r.CreatedAt).ThenBy(r => r.Id).Select(r => new BotReferenceDto(r.Id, BotMapper.ReferenceUrl(bot.Id, r.Id))).ToList(),
        bot.Looks);
}
