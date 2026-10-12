using Mediator;
using Microsoft.EntityFrameworkCore;
using NeuralDamage.Infrastructure;
using NeuralDamage.Infrastructure.Dtos;
using NeuralDamage.Infrastructure.Mappers;
using NeuralDamage.Infrastructure.Models;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.Attachments;

namespace NeuralDamage.Application.Queries;

/// <summary>The whole shared library, newest first.</summary>
public record GetStickersQuery(Guid RequestingUserId) : IQuery<Result<List<StickerDto>>>;

public class GetStickersHandler(NeuralDamageDbContext db, AppAdmins admins) : IQueryHandler<GetStickersQuery, Result<List<StickerDto>>>
{
    public async ValueTask<Result<List<StickerDto>>> Handle(GetStickersQuery request, CancellationToken cancellationToken)
    {
        var email = await db.Users.Where(u => u.Id == request.RequestingUserId).Select(u => u.Email).FirstOrDefaultAsync(cancellationToken);
        var admin = admins.IsAdmin(email);
        var stickers = await db.Stickers
            .Where(s => s.DeletedAt == null)
            .Include(s => s.UploaderUser)
            .OrderByDescending(s => s.CreatedAt)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        return Result.Success(stickers.Select(s => s.ToDto(admin || s.UploaderUserId == request.RequestingUserId)).ToList());
    }
}

/// <summary>The stickers the user sent last, most recent first, for the picker's Recent row.</summary>
public record GetRecentStickersQuery(Guid UserId, int Limit = 16) : IQuery<Result<List<Guid>>>;

public class GetRecentStickersHandler(NeuralDamageDbContext db) : IQueryHandler<GetRecentStickersQuery, Result<List<Guid>>>
{
    public async ValueTask<Result<List<Guid>>> Handle(GetRecentStickersQuery request, CancellationToken cancellationToken)
    {
        var sent = await db.Messages
            .Where(m => m.SenderUserId == request.UserId && m.StickerId != null && m.Sticker!.DeletedAt == null)
            .OrderByDescending(m => m.CreatedAt)
            .Take(request.Limit * 4)
            .Select(m => m.StickerId!.Value)
            .ToListAsync(cancellationToken);
        return Result.Success(sent.Distinct().Take(request.Limit).ToList());
    }
}

public record StickerFile(byte[] Content, string ContentType);

/// <summary>
/// A sticker's image, for anyone signed in, deleted ones included: messages
/// that carry one keep showing it.
/// </summary>
public record GetStickerImageQuery(Guid StickerId) : IQuery<Result<StickerFile>>;

public class GetStickerImageHandler(IAttachmentStorage storage) : IQueryHandler<GetStickerImageQuery, Result<StickerFile>>
{
    public async ValueTask<Result<StickerFile>> Handle(GetStickerImageQuery request, CancellationToken cancellationToken)
    {
        var data = await storage.ReadStickerAsync(request.StickerId, cancellationToken);
        // Sniffed again rather than trusted from the upload, so the type sent is always what the bytes are.
        return data is not null && ImageInspector.Inspect(data) is { } image
            ? Result.Success(new StickerFile(data, image.ContentType))
            : Result.Failure<StickerFile>("Sticker not found.");
    }
}
