using Mediator;
using Microsoft.EntityFrameworkCore;
using NeuralDamage.Infrastructure;
using NeuralDamage.Infrastructure.Dtos;
using NeuralDamage.Infrastructure.Mappers;
using NeuralDamage.Infrastructure.Models;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.Attachments;

namespace NeuralDamage.Application.Queries;

/// <summary>
/// One page of the shared library, newest first. <paramref name="Search"/>
/// finds stickers by name ("cat lol" finds cat-lol) or by description.
/// </summary>
public record GetStickersQuery(Guid RequestingUserId, string? Search = null, int Skip = 0, int Take = GetStickersQuery.DefaultTake)
    : IQuery<Result<StickerPageDto>>
{
    public const int DefaultTake = 60;
    public const int MaxTake = 200;
}

public class GetStickersHandler(NeuralDamageDbContext db, AppAdmins admins) : IQueryHandler<GetStickersQuery, Result<StickerPageDto>>
{
    public async ValueTask<Result<StickerPageDto>> Handle(GetStickersQuery request, CancellationToken cancellationToken)
    {
        var admin = await Admins.IsAdminAsync(db, admins, request.RequestingUserId, cancellationToken);
        var query = db.Stickers.Where(s => s.DeletedAt == null);

        var search = (request.Search ?? "").Trim().ToLowerInvariant();
        if (search.Length > 0)
        {
            var asName = string.Join('-', search.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            query = query.Where(s => s.Name.Contains(asName) || (s.Description != null && s.Description.ToLower().Contains(search)));
        }

        var total = await query.CountAsync(cancellationToken);
        var stickers = await query
            .OrderByDescending(s => s.CreatedAt)
            .ThenByDescending(s => s.Id)
            .Skip(Math.Max(0, request.Skip))
            .Take(Math.Clamp(request.Take, 1, GetStickersQuery.MaxTake))
            .Include(s => s.UploaderUser)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        return Result.Success(new StickerPageDto(
            stickers.Select(s => s.ToDto(admin || s.UploaderUserId == request.RequestingUserId)).ToList(),
            total));
    }
}

/// <summary>
/// The given stickers that are still in the library, in the order asked; for
/// refreshing the ones still being described.
/// </summary>
public record GetStickersByIdQuery(Guid RequestingUserId, IReadOnlyList<Guid> Ids) : IQuery<Result<List<StickerDto>>>
{
    public const int MaxIds = 100;
}

public class GetStickersByIdHandler(NeuralDamageDbContext db, AppAdmins admins) : IQueryHandler<GetStickersByIdQuery, Result<List<StickerDto>>>
{
    public async ValueTask<Result<List<StickerDto>>> Handle(GetStickersByIdQuery request, CancellationToken cancellationToken)
    {
        var ids = request.Ids.Distinct().Take(GetStickersByIdQuery.MaxIds).ToList();
        if (ids.Count == 0)
            return Result.Success(new List<StickerDto>());
        var admin = await Admins.IsAdminAsync(db, admins, request.RequestingUserId, cancellationToken);
        var stickers = await db.Stickers
            .Where(s => s.DeletedAt == null && ids.Contains(s.Id))
            .Include(s => s.UploaderUser)
            .AsNoTracking()
            .ToDictionaryAsync(s => s.Id, cancellationToken);
        return Result.Success(ids
            .Where(stickers.ContainsKey)
            .Select(id => stickers[id].ToDto(admin || stickers[id].UploaderUserId == request.RequestingUserId))
            .ToList());
    }
}

/// <summary>How big the library is, how much of it is still being described, and whether the caller may import.</summary>
public record GetStickerLibraryStatusQuery(Guid RequestingUserId) : IQuery<Result<StickerLibraryStatusDto>>;

public class GetStickerLibraryStatusHandler(NeuralDamageDbContext db, AppAdmins admins)
    : IQueryHandler<GetStickerLibraryStatusQuery, Result<StickerLibraryStatusDto>>
{
    public async ValueTask<Result<StickerLibraryStatusDto>> Handle(GetStickerLibraryStatusQuery request, CancellationToken cancellationToken)
    {
        var live = db.Stickers.Where(s => s.DeletedAt == null);
        var count = await live.CountAsync(cancellationToken);
        var describing = await live.CountAsync(s => s.Describing && s.Description == null, cancellationToken);
        var admin = await Admins.IsAdminAsync(db, admins, request.RequestingUserId, cancellationToken);
        return Result.Success(new StickerLibraryStatusDto(count, describing, admin));
    }
}

/// <summary>The stickers the user sent last, most recent first, for the picker's Recent row.</summary>
public record GetRecentStickersQuery(Guid UserId, int Limit = 16) : IQuery<Result<List<StickerDto>>>;

public class GetRecentStickersHandler(NeuralDamageDbContext db, AppAdmins admins) : IQueryHandler<GetRecentStickersQuery, Result<List<StickerDto>>>
{
    public async ValueTask<Result<List<StickerDto>>> Handle(GetRecentStickersQuery request, CancellationToken cancellationToken)
    {
        var sent = await db.Messages
            .Where(m => m.SenderUserId == request.UserId && m.StickerId != null && m.Sticker!.DeletedAt == null)
            .OrderByDescending(m => m.CreatedAt)
            .Take(request.Limit * 4)
            .Select(m => m.StickerId!.Value)
            .ToListAsync(cancellationToken);
        var ids = sent.Distinct().Take(request.Limit).ToList();
        return await new GetStickersByIdHandler(db, admins).Handle(new GetStickersByIdQuery(request.UserId, ids), cancellationToken);
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
