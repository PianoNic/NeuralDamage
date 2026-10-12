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
