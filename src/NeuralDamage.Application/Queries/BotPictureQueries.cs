using Mediator;
using NeuralDamage.Infrastructure.Dtos;
using NeuralDamage.Infrastructure.Models;
using NeuralDamage.Infrastructure.Services.BotPictures;

namespace NeuralDamage.Application.Queries;

/// <summary>Pictures a new bot called <paramref name="Name"/> could have, from Wikipedia and Commons.</summary>
public record GetBotPictureSuggestionsQuery(string Name) : IQuery<Result<List<BotPictureSuggestionDto>>>;

public class GetBotPictureSuggestionsHandler(IBotPictureFinder finder)
    : IQueryHandler<GetBotPictureSuggestionsQuery, Result<List<BotPictureSuggestionDto>>>
{
    public const int MaxNameLength = 128;

    public async ValueTask<Result<List<BotPictureSuggestionDto>>> Handle(GetBotPictureSuggestionsQuery request, CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim() ?? "";
        if (name.Length == 0 || name.Length > MaxNameLength)
            return Result.Failure<List<BotPictureSuggestionDto>>($"A name of 1 to {MaxNameLength} characters is needed.");

        var found = await finder.FindAsync(name, cancellationToken);
        return Result.Success(found.Select((c, i) => new BotPictureSuggestionDto(i, c.Title, c.SourceUrl)).ToList());
    }
}

/// <summary>
/// One suggested picture's bytes, by the name it was suggested for and its index, so the client
/// never hands the server a URL to fetch.
/// </summary>
public record GetBotPictureSuggestionImageQuery(string Name, int Index) : IQuery<Result<BotAvatarFile>>;

public class GetBotPictureSuggestionImageHandler(IBotPictureFinder finder)
    : IQueryHandler<GetBotPictureSuggestionImageQuery, Result<BotAvatarFile>>
{
    public const string NotFound = "That picture is not available.";

    public async ValueTask<Result<BotAvatarFile>> Handle(GetBotPictureSuggestionImageQuery request, CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim() ?? "";
        if (name.Length == 0 || name.Length > GetBotPictureSuggestionsHandler.MaxNameLength)
            return Result.Failure<BotAvatarFile>(NotFound);

        var found = await finder.FindAsync(name, cancellationToken);
        if (request.Index < 0 || request.Index >= found.Count)
            return Result.Failure<BotAvatarFile>(NotFound);

        var image = await finder.DownloadAsync(found[request.Index], cancellationToken);
        return image is null
            ? Result.Failure<BotAvatarFile>(NotFound)
            : Result.Success(new BotAvatarFile(image.Content, image.ContentType));
    }
}
