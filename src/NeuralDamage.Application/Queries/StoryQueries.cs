using Mediator;
using Microsoft.EntityFrameworkCore;
using NeuralDamage.Infrastructure;
using NeuralDamage.Infrastructure.Dtos;
using NeuralDamage.Infrastructure.Models;
using NeuralDamage.Infrastructure.Services.Attachments;
using NeuralDamage.Infrastructure.Services.Stories;

namespace NeuralDamage.Application.Queries;

/// <summary>
/// The stories row: the reader's own first, then everyone with a story they
/// have not seen, newest first, then those they have seen.
/// </summary>
public record GetStoriesQuery(Guid UserId) : IQuery<Result<List<StoryRingDto>>>;

public class GetStoriesHandler(NeuralDamageDbContext db) : IQueryHandler<GetStoriesQuery, Result<List<StoryRingDto>>>
{
    public async ValueTask<Result<List<StoryRingDto>>> Handle(GetStoriesQuery request, CancellationToken cancellationToken)
    {
        var ids = await db.Stories.Live(DateTime.UtcNow).Select(s => s.Id).ToListAsync(cancellationToken);
        var stories = await StoryReads.StoriesAsync(db, ids, request.UserId, cancellationToken);
        var rings = stories
            .GroupBy(s => (s.Author.Id, s.Author.IsBot))
            .Select(g => new StoryRingDto(g.First().Author, g.OrderBy(s => s.CreatedAt).ToList(), g.All(s => s.Seen), !g.Key.IsBot && g.Key.Id == request.UserId))
            .OrderByDescending(r => r.IsMine)
            .ThenBy(r => r.AllSeen)
            .ThenByDescending(r => r.Stories.Max(s => s.CreatedAt))
            .ToList();
        return Result<List<StoryRingDto>>.Success(rings);
    }
}

/// <summary>A story's PNG, while it lives.</summary>
public record GetStoryImageQuery(Guid StoryId) : IQuery<Result<byte[]>>;

public class GetStoryImageHandler(NeuralDamageDbContext db, IAttachmentStorage storage) : IQueryHandler<GetStoryImageQuery, Result<byte[]>>
{
    public async ValueTask<Result<byte[]>> Handle(GetStoryImageQuery request, CancellationToken cancellationToken)
    {
        if (!await db.Stories.Live(DateTime.UtcNow).AnyAsync(s => s.Id == request.StoryId, cancellationToken))
            return Result<byte[]>.Failure("That story is gone.");
        return await storage.ReadStoryFileAsync(request.StoryId, cancellationToken) is { } png
            ? Result<byte[]>.Success(png)
            : Result<byte[]>.Failure("That story is gone.");
    }
}
