using Mediator;
using Microsoft.EntityFrameworkCore;
using NeuralDamage.Infrastructure;
using NeuralDamage.Infrastructure.Models;
using NeuralDamage.Infrastructure.Services.Attachments;

namespace NeuralDamage.Application.Queries;

public record AttachmentFile(Stream Content, string ContentType);

/// <summary>An image's bytes, for members of its chat only.</summary>
public record GetAttachmentQuery(Guid ChatId, Guid AttachmentId, Guid RequestingUserId) : IQuery<Result<AttachmentFile>>;

public class GetAttachmentHandler(NeuralDamageDbContext db, IAttachmentStorage storage) : IQueryHandler<GetAttachmentQuery, Result<AttachmentFile>>
{
    public const string NotFound = "Image not found.";

    public async ValueTask<Result<AttachmentFile>> Handle(GetAttachmentQuery request, CancellationToken cancellationToken)
    {
        // Not being a member reads the same as the image not existing, so ids
        // cannot be probed from outside the chat.
        var isMember = await db.ChatMembers.AnyAsync(cm => cm.ChatId == request.ChatId && cm.UserId == request.RequestingUserId, cancellationToken);
        if (!isMember)
            return Result.Failure<AttachmentFile>(NotFound);

        var attachment = await db.Attachments.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == request.AttachmentId && a.ChatId == request.ChatId, cancellationToken);
        // An upload not sent yet is only its uploader's.
        if (attachment is null || (attachment.MessageId is null && attachment.UploaderUserId != request.RequestingUserId))
            return Result.Failure<AttachmentFile>(NotFound);

        var stream = storage.OpenRead(attachment.ChatId, attachment.Id);
        return stream is null
            ? Result.Failure<AttachmentFile>(NotFound)
            : Result.Success(new AttachmentFile(stream, attachment.ContentType));
    }
}
