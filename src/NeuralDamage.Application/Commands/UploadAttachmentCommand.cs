using Mediator;
using Microsoft.EntityFrameworkCore;
using NeuralDamage.Domain;
using NeuralDamage.Infrastructure;
using NeuralDamage.Infrastructure.Dtos;
using NeuralDamage.Infrastructure.Mappers;
using NeuralDamage.Infrastructure.Models;
using NeuralDamage.Infrastructure.Services.Attachments;

namespace NeuralDamage.Application.Commands;

/// <summary>
/// Stores an image for a message the user is about to send, and starts
/// describing it straight away so the description is usually ready by the
/// time the bots answer.
/// </summary>
public record UploadAttachmentCommand(Guid ChatId, Guid UploaderUserId, byte[] Data) : ICommand<Result<AttachmentDto>>;

public class UploadAttachmentHandler(NeuralDamageDbContext db, IAttachmentStorage storage, IImageDescriber describer, AttachmentOptions options)
    : ICommandHandler<UploadAttachmentCommand, Result<AttachmentDto>>
{
    public async ValueTask<Result<AttachmentDto>> Handle(UploadAttachmentCommand request, CancellationToken cancellationToken)
    {
        var isMember = await db.ChatMembers.AnyAsync(cm => cm.ChatId == request.ChatId && cm.UserId == request.UploaderUserId, cancellationToken);
        if (!isMember)
            return Result.Failure<AttachmentDto>("You are not a member of this chat.");

        if (request.Data.Length == 0)
            return Result.Failure<AttachmentDto>("The file is empty.");
        if (request.Data.Length > options.MaxBytes)
            return Result.Failure<AttachmentDto>($"Images can be at most {options.MaxBytes / (1024 * 1024.0):0.#} MB.");

        if (ImageInspector.Inspect(request.Data) is not { } image)
            return Result.Failure<AttachmentDto>("Only PNG, JPEG, WebP and GIF images can be attached.");

        var attachment = new Attachment
        {
            ChatId = request.ChatId,
            UploaderUserId = request.UploaderUserId,
            ContentType = image.ContentType,
            SizeBytes = request.Data.Length,
            Width = image.Width,
            Height = image.Height,
        };

        await storage.SaveAsync(request.ChatId, attachment.Id, request.Data, cancellationToken);
        db.Attachments.Add(attachment);
        await db.SaveChangesAsync(cancellationToken);

        describer.Enqueue(attachment.Id);
        return Result.Success(attachment.ToDto());
    }
}
