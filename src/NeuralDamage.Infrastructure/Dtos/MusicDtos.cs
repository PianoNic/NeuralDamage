namespace NeuralDamage.Infrastructure.Dtos;

/// <summary>A song Deezer found, not necessarily stored yet.</summary>
/// <param name="PreviewUrl">Deezer's 30-second preview, to listen in the picker; it stops working after a while.</param>
public record SongResultDto(long DeezerId, string Title, string Artist, string? Album, string? CoverUrl, int DurationSeconds, string? PreviewUrl);

/// <summary>A stored song, as the picker sees it.</summary>
/// <param name="PreviewUrl">The stored preview.</param>
/// <param name="FullUrl">The whole song, once it is stored; then a clip may start anywhere in it.</param>
/// <param name="FullSeconds">The whole song's length as stored.</param>
/// <param name="FullPending">The whole song is on its way: ask again in a moment.</param>
public record SongDto(Guid Id, long DeezerId, string Title, string Artist, string? Album, string? CoverUrl, int DurationSeconds,
    string? PreviewUrl, string? FullUrl, int FullSeconds, bool FullPending);

/// <summary>The song a post or story plays.</summary>
/// <param name="AudioUrl">What to play; it needs no sign-in, so an audio element can stream and seek it.</param>
/// <param name="Start">Where in it the clip starts.</param>
/// <param name="Seconds">How long the clip plays before it starts over.</param>
public record MusicDto(Guid SongId, string Title, string Artist, string? CoverUrl, string AudioUrl, int Start, int Seconds);

