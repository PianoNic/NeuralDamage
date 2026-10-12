namespace NeuralDamage.Infrastructure.Dtos;

/// <summary>
/// A sticker in the shared library. <see cref="Url"/> is relative to the API
/// and needs the caller's token. Without a <see cref="Description"/> it works
/// for people but bots do not know it.
/// </summary>
/// <param name="Describing">True while the describer is still writing its description.</param>
/// <param name="CanEdit">Whether the caller may edit or delete it: its uploader, or an admin.</param>
public record StickerDto(
    Guid Id,
    string Name,
    string Url,
    string ContentType,
    int? Width,
    int? Height,
    bool Animated,
    string? Description,
    bool Describing,
    int UseCount,
    Guid UploaderUserId,
    string UploaderName,
    DateTime CreatedAt,
    bool CanEdit);

/// <summary>The sticker a message carries.</summary>
public record MessageStickerDto(
    Guid Id,
    string Name,
    string Url,
    int? Width,
    int? Height,
    bool Animated,
    string? Description);

/// <summary>One page of the library, newest first.</summary>
/// <param name="Total">How many stickers match the search, across all pages.</param>
public record StickerPageDto(List<StickerDto> Items, int Total);

/// <summary>The library at a glance.</summary>
/// <param name="Count">How many stickers it holds.</param>
/// <param name="Describing">How many still wait for, or are getting, their description.</param>
/// <param name="CanImport">Whether the caller is an admin, who may import many at once.</param>
public record StickerLibraryStatusDto(int Count, int Describing, bool CanImport);

/// <summary>What one import request did with its files.</summary>
/// <param name="Duplicates">Files skipped because the library already has the very same image.</param>
public record StickerImportResultDto(int Added, int Duplicates, List<StickerImportFailureDto> Failed);

public record StickerImportFailureDto(string FileName, string Error);
