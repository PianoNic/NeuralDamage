using Mediator;
using Microsoft.EntityFrameworkCore;
using NeuralDamage.Infrastructure;
using NeuralDamage.Infrastructure.Models;
using NeuralDamage.Infrastructure.Services.Attachments;

namespace NeuralDamage.Application.Queries;

/// <summary>
/// One of a bot's reference pictures, for its creator only: unlike the
/// profile picture, they are never shown to anyone else.
/// </summary>
public record GetBotReferenceQuery(Guid BotId, Guid ReferenceId, Guid RequestingUserId) : IQuery<Result<BotAvatarFile>>;

public class GetBotReferenceHandler(NeuralDamageDbContext db, IAttachmentStorage storage) : IQueryHandler<GetBotReferenceQuery, Result<BotAvatarFile>>
{
    public const string NotFound = "Picture not found.";

    public async ValueTask<Result<BotAvatarFile>> Handle(GetBotReferenceQuery request, CancellationToken cancellationToken)
    {
        var mine = await db.BotReferences.AnyAsync(r => r.Id == request.ReferenceId && r.BotId == request.BotId
            && r.Bot.CreatedById == request.RequestingUserId, cancellationToken);
        if (!mine)
            return Result.Failure<BotAvatarFile>(NotFound);

        var data = await storage.ReadBotReferenceAsync(request.BotId, request.ReferenceId, cancellationToken);
        return data is not null && ImageInspector.Inspect(data) is { } image
            ? Result.Success(new BotAvatarFile(data, image.ContentType))
            : Result.Failure<BotAvatarFile>(NotFound);
    }
}
