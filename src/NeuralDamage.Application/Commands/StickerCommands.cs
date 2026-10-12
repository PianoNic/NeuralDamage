using Mediator;
using Microsoft.EntityFrameworkCore;
using NeuralDamage.Domain;
using NeuralDamage.Infrastructure;
using NeuralDamage.Infrastructure.Dtos;
using NeuralDamage.Infrastructure.Mappers;
using NeuralDamage.Infrastructure.Models;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.Attachments;
using NeuralDamage.Infrastructure.Services.Stickers;

namespace NeuralDamage.Application.Commands;

/// <summary>
/// Adds a sticker to the shared library and starts describing it. The web app
/// scales still images down first; animated ones come as they are. The name is
/// made from <paramref name="Name"/> (usually the file name), with a number
/// added when it is taken.
/// </summary>
public record UploadStickerCommand(Guid UploaderUserId, string? Name, byte[] Data) : ICommand<Result<StickerDto>>;

public class UploadStickerHandler(NeuralDamageDbContext db, IAttachmentStorage storage, IImageDescriber describer)
    : ICommandHandler<UploadStickerCommand, Result<StickerDto>>
{
    public async ValueTask<Result<StickerDto>> Handle(UploadStickerCommand request, CancellationToken cancellationToken)
    {
        if (request.Data.Length == 0)
            return Result.Failure<StickerDto>("The file is empty.");
        if (request.Data.Length > StickerRules.MaxBytes)
            return Result.Failure<StickerDto>($"Stickers can be at most {StickerRules.MaxBytes / (1024 * 1024)} MB.");

        if (StickerRules.Inspect(request.Data) is not { } inspected)
            return Result.Failure<StickerDto>("Stickers have to be PNG, WebP or GIF images.");
        var (image, animated) = inspected;
        var maxSide = animated ? StickerRules.MaxAnimatedSide : StickerRules.MaxStaticSide;
        if (image.Width > maxSide || image.Height > maxSide)
            return Result.Failure<StickerDto>($"{(animated ? "Animated stickers" : "Stickers")} can be at most {maxSide}×{maxSide} pixels.");

        var uploader = await db.Users.FirstOrDefaultAsync(u => u.Id == request.UploaderUserId, cancellationToken);
        if (uploader is null)
            return Result.Failure<StickerDto>("User not found.");

        var sticker = new Sticker
        {
            Name = await FreeNameAsync(StickerRules.Slugify(request.Name) ?? "sticker", cancellationToken),
            ContentType = image.ContentType,
            SizeBytes = request.Data.Length,
            Width = image.Width,
            Height = image.Height,
            Animated = animated,
            Describing = true,
            UploaderUserId = uploader.Id,
            UploaderUser = uploader,
        };

        await storage.SaveStickerAsync(sticker.Id, request.Data, cancellationToken);
        db.Stickers.Add(sticker);
        await db.SaveChangesAsync(cancellationToken);

        describer.EnqueueSticker(sticker.Id);
        return Result.Success(sticker.ToDto(canEdit: true));
    }

    /// <summary>"cat", then "cat-2", "cat-3"…</summary>
    private async Task<string> FreeNameAsync(string wanted, CancellationToken ct)
    {
        var taken = await db.Stickers
            .Where(s => s.DeletedAt == null && s.Name.StartsWith(wanted))
            .Select(s => s.Name)
            .ToListAsync(ct);
        if (!taken.Contains(wanted))
            return wanted;
        for (var n = 2; ; n++)
        {
            var suffix = $"-{n}";
            var candidate = (wanted.Length + suffix.Length > StickerRules.MaxNameLength
                ? wanted[..(StickerRules.MaxNameLength - suffix.Length)].TrimEnd('-')
                : wanted) + suffix;
            if (!taken.Contains(candidate) && !await db.Stickers.AnyAsync(s => s.DeletedAt == null && s.Name == candidate, ct))
                return candidate;
        }
    }
}

/// <summary>
/// Renames a sticker or rewrites its description; only its uploader or an
/// admin can. An empty description hides it from the bots again.
/// </summary>
public record UpdateStickerCommand(Guid StickerId, Guid RequestingUserId, string Name, string? Description) : ICommand<Result<StickerDto>>;

public class UpdateStickerHandler(NeuralDamageDbContext db, AppAdmins admins) : ICommandHandler<UpdateStickerCommand, Result<StickerDto>>
{
    public async ValueTask<Result<StickerDto>> Handle(UpdateStickerCommand request, CancellationToken cancellationToken)
    {
        var sticker = await db.Stickers.Include(s => s.UploaderUser)
            .FirstOrDefaultAsync(s => s.Id == request.StickerId && s.DeletedAt == null, cancellationToken);
        if (sticker is null)
            return Result.Failure<StickerDto>(StickerAccess.NotFound);
        if (!await StickerAccess.CanEditAsync(db, admins, sticker, request.RequestingUserId, cancellationToken))
            return Result.Failure<StickerDto>(StickerAccess.NotYours);

        var name = (request.Name ?? "").Trim().ToLowerInvariant();
        if (!StickerRules.IsValidName(name))
            return Result.Failure<StickerDto>($"A sticker name is lowercase letters, digits and hyphens, at most {StickerRules.MaxNameLength} characters.");
        if (name != sticker.Name && await db.Stickers.AnyAsync(s => s.DeletedAt == null && s.Name == name && s.Id != sticker.Id, cancellationToken))
            return Result.Failure<StickerDto>($"There already is a sticker called {name}.");

        var description = request.Description?.Trim();
        if (description?.Length > StickerRules.MaxDescriptionLength)
            return Result.Failure<StickerDto>($"A description can be at most {StickerRules.MaxDescriptionLength} characters.");

        sticker.Name = name;
        sticker.Description = string.IsNullOrEmpty(description) ? null : description;
        sticker.Describing = false;
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success(sticker.ToDto(canEdit: true));
    }
}

/// <summary>
/// Takes a sticker out of the library; only its uploader or an admin can.
/// Messages that carry it keep showing it.
/// </summary>
public record DeleteStickerCommand(Guid StickerId, Guid RequestingUserId) : ICommand<Result>;

public class DeleteStickerHandler(NeuralDamageDbContext db, AppAdmins admins) : ICommandHandler<DeleteStickerCommand, Result>
{
    public async ValueTask<Result> Handle(DeleteStickerCommand request, CancellationToken cancellationToken)
    {
        var sticker = await db.Stickers.FirstOrDefaultAsync(s => s.Id == request.StickerId && s.DeletedAt == null, cancellationToken);
        if (sticker is null)
            return Result.Failure(StickerAccess.NotFound);
        if (!await StickerAccess.CanEditAsync(db, admins, sticker, request.RequestingUserId, cancellationToken))
            return Result.Failure(StickerAccess.NotYours);

        sticker.DeletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

public static class StickerAccess
{
    public const string NotFound = "Sticker not found.";
    public const string NotYours = "Only whoever uploaded a sticker, or an admin, can change it.";

    public static async Task<bool> CanEditAsync(NeuralDamageDbContext db, AppAdmins admins, Sticker sticker, Guid userId, CancellationToken ct)
    {
        if (sticker.UploaderUserId == userId)
            return true;
        var email = await db.Users.Where(u => u.Id == userId).Select(u => u.Email).FirstOrDefaultAsync(ct);
        return admins.IsAdmin(email);
    }
}
