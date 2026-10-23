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
/// Adds a sticker to the shared library and queues it for describing. The web
/// app scales still images down first; animated ones come as they are. The name
/// is made from <paramref name="Name"/> (usually the file name), with a number
/// added when it is taken. An image the library already has is refused.
/// </summary>
public record UploadStickerCommand(Guid UploaderUserId, string? Name, byte[] Data) : ICommand<Result<StickerDto>>;

public class UploadStickerHandler(NeuralDamageDbContext db, IAttachmentStorage storage, IStickerDescribeQueue queue)
    : ICommandHandler<UploadStickerCommand, Result<StickerDto>>
{
    public async ValueTask<Result<StickerDto>> Handle(UploadStickerCommand request, CancellationToken cancellationToken)
    {
        var uploader = await db.Users.FirstOrDefaultAsync(u => u.Id == request.UploaderUserId, cancellationToken);
        if (uploader is null)
            return Result.Failure<StickerDto>("User not found.");

        var added = await StickerAdder.AddAsync(db, storage, queue, uploader, request.Name, request.Data, cancellationToken);
        if (added.Duplicate is { } existing)
            return Result.Failure<StickerDto>($"The library already has this sticker, as {existing}.");
        return added.Error is { } error
            ? Result.Failure<StickerDto>(error)
            : Result.Success(added.Sticker!.ToDto(canEdit: true));
    }
}

public record StickerFileUpload(string FileName, byte[] Data);

/// <summary>
/// An admin adds many stickers at once, such as a whole WhatsApp sticker
/// folder. Files the library already has are skipped, and one bad file does
/// not stop the rest. Describing them goes through the describe queue.
/// </summary>
public record ImportStickersCommand(Guid UserId, IReadOnlyList<StickerFileUpload> Files) : ICommand<Result<StickerImportResultDto>>;

public class ImportStickersHandler(NeuralDamageDbContext db, IAttachmentStorage storage, IStickerDescribeQueue queue, AppAdmins admins)
    : ICommandHandler<ImportStickersCommand, Result<StickerImportResultDto>>
{
    public const string AdminsOnly = "Only an admin can import stickers.";

    public async ValueTask<Result<StickerImportResultDto>> Handle(ImportStickersCommand request, CancellationToken cancellationToken)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == request.UserId, cancellationToken);
        if (user is null || !(user.IsAdmin || admins.IsAdmin(user.Email)))
            return Result.Failure<StickerImportResultDto>(AdminsOnly);

        int addedCount = 0, duplicates = 0;
        var failed = new List<StickerImportFailureDto>();
        foreach (var file in request.Files)
        {
            var added = await StickerAdder.AddAsync(db, storage, queue, user, Path.GetFileNameWithoutExtension(file.FileName), file.Data, cancellationToken);
            if (added.Sticker is not null)
                addedCount++;
            else if (added.Duplicate is not null)
                duplicates++;
            else
                failed.Add(new StickerImportFailureDto(file.FileName, added.Error!));
        }
        return Result.Success(new StickerImportResultDto(addedCount, duplicates, failed));
    }
}

/// <summary>What adding one file came to: the new sticker, the name of the one it duplicates, or why it was refused.</summary>
public record StickerAdded(Sticker? Sticker, string? Duplicate, string? Error);

public static class StickerAdder
{
    public static async Task<StickerAdded> AddAsync(
        NeuralDamageDbContext db, IAttachmentStorage storage, IStickerDescribeQueue queue, User uploader, string? name, byte[] data, CancellationToken ct)
    {
        if (data.Length == 0)
            return new(null, null, "The file is empty.");
        if (data.Length > StickerRules.MaxBytes)
            return new(null, null, $"Stickers can be at most {StickerRules.MaxBytes / (1024 * 1024)} MB.");

        if (StickerRules.Inspect(data) is not { } inspected)
            return new(null, null, "Stickers have to be PNG, WebP or GIF images.");
        var (image, animated) = inspected;
        var maxSide = animated ? StickerRules.MaxAnimatedSide : StickerRules.MaxStaticSide;
        if (image.Width > maxSide || image.Height > maxSide)
            return new(null, null, $"{(animated ? "Animated stickers" : "Stickers")} can be at most {maxSide}×{maxSide} pixels.");

        // Stickers loop forever, as in WhatsApp, whatever loop count the file came with.
        if (animated)
            data = StickerRules.LoopForever(data);

        var hash = StickerRules.Hash(data);
        var duplicate = await db.Stickers
            .Where(s => s.DeletedAt == null && s.Sha256 == hash)
            .Select(s => s.Name)
            .FirstOrDefaultAsync(ct);
        if (duplicate is not null)
            return new(null, duplicate, null);

        var sticker = new Sticker
        {
            Name = await FreeNameAsync(db, StickerRules.Slugify(name) ?? "sticker", ct),
            ContentType = image.ContentType,
            SizeBytes = data.Length,
            Width = image.Width,
            Height = image.Height,
            Animated = animated,
            Sha256 = hash,
            Describing = true,
            UploaderUserId = uploader.Id,
            UploaderUser = uploader,
        };

        await storage.SaveStickerAsync(sticker.Id, data, ct);
        db.Stickers.Add(sticker);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Someone took the name in the meantime; the next file must not trip over this one.
            db.Entry(sticker).State = EntityState.Detached;
            return new(null, null, "It could not be saved. Try again.");
        }

        queue.Enqueue(sticker.Id);
        return new(sticker, null, null);
    }

    /// <summary>"cat", then "cat-2", "cat-3"…</summary>
    private static async Task<string> FreeNameAsync(NeuralDamageDbContext db, string wanted, CancellationToken ct)
    {
        var taken = (await db.Stickers
            .Where(s => s.DeletedAt == null && s.Name.StartsWith(wanted))
            .Select(s => s.Name)
            .ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
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
        return sticker.UploaderUserId == userId || await Admins.IsAdminAsync(db, admins, userId, ct);
    }
}
