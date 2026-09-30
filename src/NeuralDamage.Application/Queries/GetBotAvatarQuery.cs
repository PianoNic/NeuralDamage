using Mediator;
using NeuralDamage.Infrastructure.Models;
using NeuralDamage.Infrastructure.Services.Attachments;

namespace NeuralDamage.Application.Queries;

public record BotAvatarFile(byte[] Content, string ContentType);

/// <summary>
/// A bot's own picture, for anyone signed in: public bots show up in any chat
/// and on the Bots page.
/// </summary>
public record GetBotAvatarQuery(Guid BotId) : IQuery<Result<BotAvatarFile>>;

public class GetBotAvatarHandler(IAttachmentStorage storage) : IQueryHandler<GetBotAvatarQuery, Result<BotAvatarFile>>
{
    public const string NotFound = "Picture not found.";

    public async ValueTask<Result<BotAvatarFile>> Handle(GetBotAvatarQuery request, CancellationToken cancellationToken)
    {
        var data = await storage.ReadBotAvatarAsync(request.BotId, cancellationToken);
        // Sniffed again rather than trusted from the upload, so the type sent is always what the bytes are.
        return data is not null && ImageInspector.Inspect(data) is { } image
            ? Result.Success(new BotAvatarFile(data, image.ContentType))
            : Result.Failure<BotAvatarFile>(NotFound);
    }
}
