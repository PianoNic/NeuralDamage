using Mediator;
using Microsoft.EntityFrameworkCore;
using NeuralDamage.Domain;
using NeuralDamage.Infrastructure;
using NeuralDamage.Infrastructure.Dtos;
using NeuralDamage.Infrastructure.Mappers;
using NeuralDamage.Infrastructure.Models;
using NeuralDamage.Infrastructure.Services.Attachments;
using NeuralDamage.Infrastructure.Services.Images;
using NeuralDamage.Infrastructure.Services.Stories;

namespace NeuralDamage.Application.Commands;

/// <summary>
/// Starts drawing a picture for a post or a story the person is making, optionally with
/// a public bot in it, its face taken from its profile picture. It answers at
/// once with the pending picture; the person polls it until it is ready.
/// </summary>
/// <param name="Format">The shape of what it is for (<see cref="StoryFormats"/>): a story's or a composed post picture's background. Null is a square post picture.</param>
public record GenerateImageCommand(Guid UserId, string Prompt, Guid? FaceBotId = null, bool ForStory = false, string? Format = null) : ICommand<Result<GeneratedImageDto>>;

public class GenerateImageHandler(NeuralDamageDbContext db, ImageGenerator generator) : ICommandHandler<GenerateImageCommand, Result<GeneratedImageDto>>
{
    public async ValueTask<Result<GeneratedImageDto>> Handle(GenerateImageCommand request, CancellationToken cancellationToken)
    {
        if (request.FaceBotId is { } faceBotId
            && !await db.Bots.AnyAsync(b => b.Id == faceBotId && b.IsActive && b.IsPublic && b.AvatarUrl != null, cancellationToken))
            return Result.Failure<GeneratedImageDto>("That bot has no picture to take a face from.");

        var format = request.ForStory ? StoryFormats.Story : request.Format?.Trim().ToLowerInvariant();
        if (format is not null && !StoryFormats.IsKnown(format))
            return Result.Failure<GeneratedImageDto>("The format must be story, post-4x5 or post-1x1.");
        var (width, height) = format is null ? (ImageGenerator.PostSize, ImageGenerator.PostSize) : StoryFormats.Picture(format);
        var started = await generator.StartAsync(new ImageAsk(request.Prompt, request.UserId, null, request.FaceBotId, width, height), cancellationToken);
        return started.IsSuccess
            ? Result.Success(GeneratedImages.ToDto(started.Value!.Image, generator.Waiting))
            : Result.Failure<GeneratedImageDto>(started.Error!);
    }
}

/// <summary>Renders a layer spec in a post's format into a picture for the post the person is writing.</summary>
public record ComposeImageCommand(Guid UserId, StorySpec Spec) : ICommand<Result<GeneratedImageDto>>;

public class ComposeImageHandler(ImageComposer composer) : ICommandHandler<ComposeImageCommand, Result<GeneratedImageDto>>
{
    public async ValueTask<Result<GeneratedImageDto>> Handle(ComposeImageCommand request, CancellationToken cancellationToken)
    {
        var composed = await composer.ComposeAsync(request.Spec, request.UserId, null, cancellationToken);
        return composed.IsSuccess
            ? Result.Success(GeneratedImages.ToDto(composed.Value!, 0))
            : Result.Failure<GeneratedImageDto>(composed.Error!);
    }
}

/// <summary>Throws away a picture the person made and did not post.</summary>
public record DiscardImageCommand(Guid UserId, Guid ImageId) : ICommand<Result>;

public class DiscardImageHandler(NeuralDamageDbContext db, IAttachmentStorage storage) : ICommandHandler<DiscardImageCommand, Result>
{
    public async ValueTask<Result> Handle(DiscardImageCommand request, CancellationToken cancellationToken)
    {
        var image = await db.GeneratedImages.FirstOrDefaultAsync(i => i.Id == request.ImageId && i.UserId == request.UserId, cancellationToken);
        if (image is null)
            return Result.Failure("That picture is gone.");
        if (await db.Posts.AnyAsync(p => p.ImageId == image.Id, cancellationToken))
            return Result.Failure("That picture is on a post; delete the post instead.");

        db.GeneratedImages.Remove(image);
        await db.SaveChangesAsync(cancellationToken);
        storage.DeleteGeneratedImage(image.Id);
        return Result.Success();
    }
}

/// <summary>The person's own picture, while it is drawn and after.</summary>
public record GetGeneratedImageQuery(Guid UserId, Guid ImageId) : IQuery<Result<GeneratedImageDto>>;

public class GetGeneratedImageHandler(NeuralDamageDbContext db, ImageGenerator generator) : IQueryHandler<GetGeneratedImageQuery, Result<GeneratedImageDto>>
{
    public async ValueTask<Result<GeneratedImageDto>> Handle(GetGeneratedImageQuery request, CancellationToken cancellationToken)
    {
        var image = await db.GeneratedImages.AsNoTracking().FirstOrDefaultAsync(i => i.Id == request.ImageId && i.UserId == request.UserId, cancellationToken);
        return image is null
            ? Result.Failure<GeneratedImageDto>("That picture is gone.")
            : Result.Success(GeneratedImages.ToDto(image, generator.Waiting));
    }
}

public record GeneratedImageFile(byte[] Content, string ContentType);

/// <summary>
/// A generated picture's file: for the person who made it, and for everyone
/// signed in once it is on a post they can see.
/// </summary>
public record GetGeneratedImageFileQuery(Guid UserId, Guid ImageId) : IQuery<Result<GeneratedImageFile>>;

public class GetGeneratedImageFileHandler(NeuralDamageDbContext db, IAttachmentStorage storage) : IQueryHandler<GetGeneratedImageFileQuery, Result<GeneratedImageFile>>
{
    public async ValueTask<Result<GeneratedImageFile>> Handle(GetGeneratedImageFileQuery request, CancellationToken cancellationToken)
    {
        var image = await db.GeneratedImages.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == request.ImageId && i.Status == GeneratedImageStatus.Ready, cancellationToken);
        if (image is null)
            return Result.Failure<GeneratedImageFile>("Image not found.");
        if (image.UserId != request.UserId && !await db.Posts.Visible().AnyAsync(p => p.ImageId == image.Id, cancellationToken))
            return Result.Failure<GeneratedImageFile>("Image not found.");

        var data = await storage.ReadGeneratedImageAsync(image.Id, cancellationToken);
        return data is null
            ? Result.Failure<GeneratedImageFile>("Image not found.")
            : Result.Success(new GeneratedImageFile(data, image.ContentType));
    }
}

public static class GeneratedImages
{
    public static GeneratedImageDto ToDto(GeneratedImage image, int waiting) => new(
        image.Id,
        image.Status,
        image.Prompt,
        image.FaceBotId,
        image.Status == GeneratedImageStatus.Ready ? FeedReads.ImageUrl(image.Id) : null,
        image.Width,
        image.Height,
        image.Error,
        image.Status == GeneratedImageStatus.Pending ? Math.Max(1, waiting) : 0,
        image.Kind,
        image.Spec is { } spec ? StorySpecRules.Parse(spec).Spec : null);
}
