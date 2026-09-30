namespace NeuralDamage.Infrastructure.Dtos;

/// <summary>
/// An image on a message. <see cref="Url"/> is relative to the API and needs
/// the caller's token; <see cref="Description"/> is the describer's text once
/// it has run, which doubles as alt text.
/// </summary>
public record AttachmentDto(
    Guid Id,
    string Url,
    string ContentType,
    long SizeBytes,
    int? Width,
    int? Height,
    string? Description);
