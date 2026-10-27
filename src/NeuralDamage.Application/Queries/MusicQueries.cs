using Mediator;
using Microsoft.EntityFrameworkCore;
using NeuralDamage.Infrastructure;
using NeuralDamage.Infrastructure.Dtos;
using NeuralDamage.Infrastructure.Mappers;
using NeuralDamage.Infrastructure.Models;
using NeuralDamage.Infrastructure.Services.Music;

namespace NeuralDamage.Application.Queries;

/// <summary>Songs for the music picker: what was searched for, or, with no search, the chart.</summary>
/// <param name="ForText">With no search: songs matching these words (the post being written) first, then the chart.</param>
public record FindSongsQuery(string? Search, string? ForText = null) : IQuery<Result<List<SongResultDto>>>;

public class FindSongsHandler(IDeezerClient deezer, SongPicker picker) : IQueryHandler<FindSongsQuery, Result<List<SongResultDto>>>
{
    public const int Limit = 20;

    public async ValueTask<Result<List<SongResultDto>>> Handle(FindSongsQuery request, CancellationToken cancellationToken)
    {
        var search = request.Search?.Trim() ?? "";
        if (search.Length > DeezerClient.MaxQueryLength)
            return Result<List<SongResultDto>>.Failure($"A search can be at most {DeezerClient.MaxQueryLength} characters.");

        IReadOnlyList<DeezerTrack> tracks;
        if (search.Length > 0)
            tracks = await deezer.SearchAsync(search, Limit, cancellationToken);
        else if (!string.IsNullOrWhiteSpace(request.ForText))
            tracks = await picker.ShortlistAsync(request.ForText, cancellationToken);
        else
            tracks = await deezer.ChartAsync(Limit, cancellationToken);
        return Result<List<SongResultDto>>.Success(tracks.Select(t => t.ToResult()).ToList());
    }
}

/// <summary>A stored song, to see whether its whole song has arrived.</summary>
public record GetSongQuery(Guid SongId) : IQuery<Result<SongDto>>;

public class GetSongHandler(NeuralDamageDbContext db, SongLibrary library) : IQueryHandler<GetSongQuery, Result<SongDto>>
{
    public async ValueTask<Result<SongDto>> Handle(GetSongQuery request, CancellationToken cancellationToken)
    {
        var song = await db.Songs.AsNoTracking().FirstOrDefaultAsync(s => s.Id == request.SongId, cancellationToken);
        return song is null ? Result<SongDto>.Failure("That song is gone.") : Result<SongDto>.Success(library.ToDto(song));
    }
}

/// <summary>Where a song's audio is on disk and what it is, to stream it.</summary>
public record SongAudioFile(string Path, string ContentType);

/// <summary>A song's stored preview or whole song.</summary>
public record GetSongAudioQuery(Guid SongId, bool Full) : IQuery<Result<SongAudioFile>>;

public class GetSongAudioHandler(NeuralDamageDbContext db, SongAudioStore store) : IQueryHandler<GetSongAudioQuery, Result<SongAudioFile>>
{
    public async ValueTask<Result<SongAudioFile>> Handle(GetSongAudioQuery request, CancellationToken cancellationToken)
    {
        var song = await db.Songs.AsNoTracking().FirstOrDefaultAsync(s => s.Id == request.SongId, cancellationToken);
        var type = song is null ? null : request.Full ? song.FullContentType : song.HasPreview ? "audio/mpeg" : null;
        var path = store.PathOf(request.SongId, request.Full);
        return type is null || !File.Exists(path)
            ? Result<SongAudioFile>.Failure("That song is gone.")
            : Result<SongAudioFile>.Success(new SongAudioFile(path, type));
    }
}
