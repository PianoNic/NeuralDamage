namespace NeuralDamage.Domain;

/// <summary>
/// A sticker in the app's one shared library: anyone can send it, people and
/// bots alike. Deleting one only takes it out of the library; messages that
/// already carry it keep showing it.
/// </summary>
public class Sticker : BaseEntity
{
    /// <summary>Lowercase words joined by hyphens, unique among live stickers; what a bot writes in <c>[sticker: name]</c>.</summary>
    public required string Name { get; set; }

    /// <summary>
    /// A short line on what it shows and when to send it, written by the
    /// describer and editable by the uploader. Bots only see stickers that have one.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>True while the describer is on it.</summary>
    public bool Describing { get; set; }

    public required string ContentType { get; init; }
    public required long SizeBytes { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public bool Animated { get; init; }

    /// <summary>
    /// The SHA-256 of the file, lowercase hex, so an import skips a sticker the
    /// library already has. Null only on stickers from before it was kept, until
    /// the describe service fills it in at the next start.
    /// </summary>
    public string? Sha256 { get; set; }

    /// <summary>How often it was sent; the most used go into the bots' prompt first.</summary>
    public int UseCount { get; set; }

    public DateTime? DeletedAt { get; set; }

    public required Guid UploaderUserId { get; init; }
    public User UploaderUser { get; set; } = null!;
}
