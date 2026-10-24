using Mediator;
using Microsoft.EntityFrameworkCore;
using NeuralDamage.Infrastructure;
using NeuralDamage.Infrastructure.Models;
using NeuralDamage.Infrastructure.Services.Attachments;

namespace NeuralDamage.Application.Queries;

/// <summary>A group chat's picture, for its members.</summary>
public record GetChatAvatarQuery(Guid ChatId, Guid RequestingUserId) : IQuery<Result<BotAvatarFile>>;

public class GetChatAvatarHandler(NeuralDamageDbContext db, IAttachmentStorage storage) : IQueryHandler<GetChatAvatarQuery, Result<BotAvatarFile>>
{
    public async ValueTask<Result<BotAvatarFile>> Handle(GetChatAvatarQuery request, CancellationToken cancellationToken)
    {
        var member = await db.ChatMembers.AnyAsync(cm => cm.ChatId == request.ChatId && cm.UserId == request.RequestingUserId, cancellationToken);
        if (!member)
            return Result.Failure<BotAvatarFile>(GetBotAvatarHandler.NotFound);

        var data = await storage.ReadChatAvatarAsync(request.ChatId, cancellationToken);
        // Sniffed again rather than trusted from the upload, as for a bot's.
        return data is not null && ImageInspector.Inspect(data) is { } image
            ? Result.Success(new BotAvatarFile(data, image.ContentType))
            : Result.Failure<BotAvatarFile>(GetBotAvatarHandler.NotFound);
    }
}
