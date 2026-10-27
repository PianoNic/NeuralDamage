using Mediator;
using NeuralDamage.Infrastructure.Dtos;
using NeuralDamage.Infrastructure.Models;
using NeuralDamage.Infrastructure.Services.Music;

namespace NeuralDamage.Application.Commands;

/// <summary>
/// Stores a song someone picked, with its preview, and starts fetching the whole song so they can
/// choose where the clip starts; ask for it again (<c>GET /api/music/songs/{id}</c>) until it is there.
/// </summary>
public record AddSongCommand(long DeezerId) : ICommand<Result<SongDto>>;

public class AddSongHandler(SongLibrary library, Microsoft.Extensions.Options.IOptions<MusicOptions> options) : ICommandHandler<AddSongCommand, Result<SongDto>>
{
    public async ValueTask<Result<SongDto>> Handle(AddSongCommand request, CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
            return Result<SongDto>.Failure("Music is turned off.");
        var song = await library.AddAsync(request.DeezerId, wantFull: true, cancellationToken);
        return song is null
            ? Result<SongDto>.Failure("That song can't be played right now.")
            : Result<SongDto>.Success(library.ToDto(song));
    }
}
