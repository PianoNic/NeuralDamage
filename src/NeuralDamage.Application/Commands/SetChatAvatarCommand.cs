using Mediator;
using Microsoft.EntityFrameworkCore;
using NeuralDamage.Infrastructure;
using NeuralDamage.Infrastructure.Mappers;
using NeuralDamage.Infrastructure.Models;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.Attachments;

namespace NeuralDamage.Application.Commands;

/// <summary>
/// Gives a group chat its own picture, replacing the one it had; the owner's to change. A direct
/// chat shows the other one's picture instead. The web app crops it square before upload.
/// Returns the chat's new avatar URL.
/// </summary>
public record SetChatAvatarCommand(Guid ChatId, Guid RequestingUserId, byte[] Data) : ICommand<Result<string>>;

public class SetChatAvatarHandler(NeuralDamageDbContext db, IAttachmentStorage storage, AttachmentOptions options, IChatNotificationService notifications)
    : ICommandHandler<SetChatAvatarCommand, Result<string>>
{
    public async ValueTask<Result<string>> Handle(SetChatAvatarCommand request, CancellationToken cancellationToken)
    {
        var chat = await db.Chats.FirstOrDefaultAsync(c => c.Id == request.ChatId, cancellationToken);
        if (chat is null)
            return Result.Failure<string>("Chat not found.");
        if (chat.IsDirect)
            return Result.Failure<string>(ChatAvatars.DirectHasNone);
        if (!await Admins.RunsChatAsync(db, chat.Id, request.RequestingUserId, cancellationToken))
            return Result.Failure<string>("Only the chat owner can change its picture.");

        if (request.Data.Length == 0)
            return Result.Failure<string>("The file is empty.");
        if (request.Data.Length > options.MaxBytes)
            return Result.Failure<string>($"Images can be at most {options.MaxBytes / (1024 * 1024.0):0.#} MB.");
        if (ImageInspector.Inspect(request.Data) is null)
            return Result.Failure<string>("Only PNG, JPEG, WebP and GIF images can be used.");

        await storage.SaveChatAvatarAsync(chat.Id, request.Data, cancellationToken);
        chat.AvatarUrl = ChatAvatars.UrlOf(chat.Id);
        await db.SaveChangesAsync(cancellationToken);

        await notifications.NotifyChatUpdated(chat.Id, chat.ToDto());
        await notifications.NotifySystemMessage(chat.Id, $"{await ChatAvatars.NameOfAsync(db, request.RequestingUserId, cancellationToken)} changed the chat's picture.");
        return Result.Success(chat.AvatarUrl);
    }
}

/// <summary>Takes a group chat's picture away, back to its members' faces.</summary>
public record RemoveChatAvatarCommand(Guid ChatId, Guid RequestingUserId) : ICommand<Result>;

public class RemoveChatAvatarHandler(NeuralDamageDbContext db, IAttachmentStorage storage, IChatNotificationService notifications)
    : ICommandHandler<RemoveChatAvatarCommand, Result>
{
    public async ValueTask<Result> Handle(RemoveChatAvatarCommand request, CancellationToken cancellationToken)
    {
        var chat = await db.Chats.FirstOrDefaultAsync(c => c.Id == request.ChatId, cancellationToken);
        if (chat is null)
            return Result.Failure("Chat not found.");
        if (!await Admins.RunsChatAsync(db, chat.Id, request.RequestingUserId, cancellationToken))
            return Result.Failure("Only the chat owner can change its picture.");
        if (chat.AvatarUrl is null)
            return Result.Success();

        storage.DeleteChatAvatar(chat.Id);
        chat.AvatarUrl = null;
        await db.SaveChangesAsync(cancellationToken);

        await notifications.NotifyChatUpdated(chat.Id, chat.ToDto());
        await notifications.NotifySystemMessage(chat.Id, $"{await ChatAvatars.NameOfAsync(db, request.RequestingUserId, cancellationToken)} removed the chat's picture.");
        return Result.Success();
    }
}

public static class ChatAvatars
{
    public const string DirectHasNone = "A direct chat shows who it is with, so it has no picture of its own.";

    /// <summary>Where the web app loads a chat's picture; the version changes with every upload, like a bot's.</summary>
    public static string UrlOf(Guid chatId) => $"/api/chats/{chatId}/avatar?v={DateTime.UtcNow.Ticks}";

    internal static async Task<string> NameOfAsync(NeuralDamageDbContext db, Guid userId, CancellationToken ct) =>
        await db.Users.Where(u => u.Id == userId).Select(u => u.DisplayName != "" ? u.DisplayName : u.Email).FirstOrDefaultAsync(ct) ?? "Someone";
}
