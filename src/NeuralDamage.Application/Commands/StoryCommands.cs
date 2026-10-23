using Mediator;
using Microsoft.EntityFrameworkCore;
using NeuralDamage.Application.Queries;
using NeuralDamage.Domain;
using NeuralDamage.Domain.Enums;
using NeuralDamage.Infrastructure;
using NeuralDamage.Infrastructure.Dtos;
using NeuralDamage.Infrastructure.Mappers;
using NeuralDamage.Infrastructure.Models;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.Attachments;
using NeuralDamage.Infrastructure.Services.Feed;
using NeuralDamage.Infrastructure.Services.Stories;

namespace NeuralDamage.Application.Commands;

public record CreateStoryCommand(Guid UserId, StorySpec Spec) : ICommand<Result<StoryDto>>;

public class CreateStoryHandler(StoryPublisher publisher) : ICommandHandler<CreateStoryCommand, Result<StoryDto>>
{
    public async ValueTask<Result<StoryDto>> Handle(CreateStoryCommand request, CancellationToken cancellationToken) =>
        await publisher.PublishAsync(request.Spec, request.UserId, null, cancellationToken);
}

/// <summary>The PNG a spec would become, without posting it: the editor's exact preview.</summary>
public record PreviewStoryCommand(Guid UserId, StorySpec Spec) : ICommand<Result<byte[]>>;

public class PreviewStoryHandler(StoryPublisher publisher) : ICommandHandler<PreviewStoryCommand, Result<byte[]>>
{
    public async ValueTask<Result<byte[]>> Handle(PreviewStoryCommand request, CancellationToken cancellationToken) =>
        await publisher.RenderAsync(request.Spec, request.UserId, cancellationToken);
}

/// <summary>A picture for a story being made: its background or an image layer.</summary>
public record UploadStoryImageCommand(Guid UserId, byte[] Data) : ICommand<Result<StoryImageUploadDto>>;

public class UploadStoryImageHandler(NeuralDamageDbContext db, IAttachmentStorage storage, AttachmentOptions options) : ICommandHandler<UploadStoryImageCommand, Result<StoryImageUploadDto>>
{
    public async ValueTask<Result<StoryImageUploadDto>> Handle(UploadStoryImageCommand request, CancellationToken cancellationToken)
    {
        if (request.Data.Length == 0)
            return Result.Failure<StoryImageUploadDto>("The file is empty.");
        if (request.Data.Length > options.MaxBytes)
            return Result.Failure<StoryImageUploadDto>($"Images can be at most {options.MaxBytes / (1024 * 1024.0):0.#} MB.");
        // Checked by its bytes, never by the name or the type the browser claimed.
        if (ImageInspector.Inspect(request.Data) is not { } image)
            return Result.Failure<StoryImageUploadDto>("Only PNG, JPEG, WebP and GIF images can be used.");

        var upload = new StoryImage
        {
            UploaderUserId = request.UserId,
            ContentType = image.ContentType,
            SizeBytes = request.Data.Length,
            Width = image.Width,
            Height = image.Height,
        };
        await storage.SaveStoryFileAsync(upload.Id, request.Data, cancellationToken);
        db.StoryImages.Add(upload);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success(new StoryImageUploadDto(upload.Id, $"upload:{upload.Id}", upload.Width, upload.Height));
    }
}

public record MarkStorySeenCommand(Guid UserId, Guid StoryId) : ICommand<Result>;

public class MarkStorySeenHandler(NeuralDamageDbContext db) : ICommandHandler<MarkStorySeenCommand, Result>
{
    public async ValueTask<Result> Handle(MarkStorySeenCommand request, CancellationToken cancellationToken)
    {
        if (!await db.Stories.AnyAsync(s => s.Id == request.StoryId, cancellationToken))
            return Result.Failure("That story is gone.");
        if (!await db.StoryViews.AnyAsync(v => v.StoryId == request.StoryId && v.UserId == request.UserId, cancellationToken))
        {
            db.StoryViews.Add(new StoryView { StoryId = request.StoryId, UserId = request.UserId });
            await db.SaveChangesAsync(cancellationToken);
        }
        return Result.Success();
    }
}

public record DeleteStoryCommand(Guid UserId, Guid StoryId) : ICommand<Result>;

public class DeleteStoryHandler(NeuralDamageDbContext db, IAttachmentStorage storage, AppAdmins admins, IFeedNotifier notifier) : ICommandHandler<DeleteStoryCommand, Result>
{
    public async ValueTask<Result> Handle(DeleteStoryCommand request, CancellationToken cancellationToken)
    {
        var story = await db.Stories.FirstOrDefaultAsync(s => s.Id == request.StoryId, cancellationToken);
        if (story is null)
            return Result.Failure("That story is gone.");
        if (story.AuthorUserId != request.UserId && !await StickerReads.IsAdminAsync(db, admins, request.UserId, cancellationToken))
            return Result.Failure("Only its author or an admin can delete a story.");

        db.Stories.Remove(story);
        await db.SaveChangesAsync(cancellationToken);
        storage.DeleteStoryFile(story.Id);
        await notifier.StoryDeleted(story.Id);
        return Result.Success();
    }
}

/// <summary>
/// A reply to a story goes into a direct chat with its author, bot or person,
/// which is made when there is none yet. The story goes along as a picture, so
/// the author (and a bot's model) sees what is being answered.
/// </summary>
public record ReplyToStoryCommand(Guid UserId, Guid StoryId, string Content) : ICommand<Result<StoryReplyResultDto>>;

public class ReplyToStoryHandler(NeuralDamageDbContext db, IAttachmentStorage storage, IChatNotificationService notifications, ISender sender) : ICommandHandler<ReplyToStoryCommand, Result<StoryReplyResultDto>>
{
    public const int MaxReplyLength = 1000;

    public async ValueTask<Result<StoryReplyResultDto>> Handle(ReplyToStoryCommand request, CancellationToken cancellationToken)
    {
        var content = request.Content.Trim();
        if (content.Length == 0)
            return Result.Failure<StoryReplyResultDto>("Write something to reply.");
        if (content.Length > MaxReplyLength)
            return Result.Failure<StoryReplyResultDto>($"A reply can be at most {MaxReplyLength} characters.");

        var story = await db.Stories.Live(DateTime.UtcNow)
            .Include(s => s.AuthorUser)
            .Include(s => s.AuthorBot)
            .FirstOrDefaultAsync(s => s.Id == request.StoryId, cancellationToken);
        if (story is null)
            return Result.Failure<StoryReplyResultDto>("That story is gone.");
        if (story.AuthorUserId == request.UserId)
            return Result.Failure<StoryReplyResultDto>("That is your own story.");

        var me = await db.Users.FirstAsync(u => u.Id == request.UserId, cancellationToken);
        var chat = await DirectChatAsync(me, story, cancellationToken);

        // The story as a picture in the chat, already described, so no describer is needed.
        Guid[] attachmentIds = [];
        if (await storage.ReadStoryFileAsync(story.Id, cancellationToken) is { } png)
        {
            var spec = StorySpecRules.Parse(story.Spec).Spec;
            var author = FeedReads.Author(story.AuthorUser, story.AuthorBot).Name;
            var attachment = new Attachment
            {
                ChatId = chat.Id,
                UploaderUserId = me.Id,
                ContentType = "image/png",
                SizeBytes = png.Length,
                Width = StoryRenderer.Width,
                Height = StoryRenderer.Height,
                Description = $"{author}'s story" + (spec is null ? "" : $": {StorySpecRules.Describe(spec)}"),
            };
            await storage.SaveAsync(chat.Id, attachment.Id, png, cancellationToken);
            db.Attachments.Add(attachment);
            await db.SaveChangesAsync(cancellationToken);
            attachmentIds = [attachment.Id];
        }

        var sent = await sender.Send(new SendMessageCommand(chat.Id, me.Id, content, AttachmentIds: attachmentIds), cancellationToken);
        return sent.IsSuccess
            ? Result.Success(new StoryReplyResultDto(chat.Id))
            : Result.Failure<StoryReplyResultDto>(sent.Error!);
    }

    /// <summary>The chat with only the two of them in it, or a new one.</summary>
    private async Task<Chat> DirectChatAsync(User me, Story story, CancellationToken ct)
    {
        var existing = await db.Chats
            .Where(c => c.Members.Count == 2
                && c.Members.Any(m => m.UserId == me.Id)
                && c.Members.Any(m => (story.AuthorBotId != null && m.BotId == story.AuthorBotId) || (story.AuthorUserId != null && m.UserId == story.AuthorUserId)))
            .OrderBy(c => c.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (existing is not null)
            return existing;

        var myName = FeedReads.Author(me, null).Name;
        var theirName = FeedReads.Author(story.AuthorUser, story.AuthorBot).Name;
        var chat = new Chat { Name = story.AuthorBotId is not null ? theirName : $"{myName} & {theirName}", CreatedById = me.Id };
        db.Chats.Add(chat);
        db.ChatMembers.Add(new ChatMember { ChatId = chat.Id, UserId = me.Id, Role = ChatMemberRole.Owner });
        db.ChatMembers.Add(story.AuthorBotId is { } botId
            ? new ChatMember { ChatId = chat.Id, BotId = botId }
            : new ChatMember { ChatId = chat.Id, UserId = story.AuthorUserId });
        await db.SaveChangesAsync(ct);

        await notifications.NotifyUserChatCreated(me.Id, chat.ToDto());
        if (story.AuthorUserId is { } other)
            await notifications.NotifyUserChatCreated(other, chat.ToDto());
        return chat;
    }
}
