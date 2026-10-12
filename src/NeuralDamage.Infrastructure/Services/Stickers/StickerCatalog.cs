using Microsoft.EntityFrameworkCore;
using NeuralDamage.Domain;

namespace NeuralDamage.Infrastructure.Services.Stickers;

/// <summary>A sticker as the bots know it.</summary>
public record BotSticker(Guid Id, string Name, string Description);

/// <summary>
/// The stickers bots can send in a round: every live sticker with a
/// description, and the few of them listed in the system prompt.
/// </summary>
public sealed class StickerCatalog
{
    /// <summary>How many go into the system prompt.</summary>
    public const int PromptLimit = 40;

    public static readonly StickerCatalog Empty = new([]);

    private readonly Dictionary<string, BotSticker> _byName;

    /// <summary>
    /// The most used, then the newest, up to <see cref="PromptLimit"/>, listed
    /// by name: the list only changes when the library does (or the top
    /// <see cref="PromptLimit"/> do), so the system prompt keeps hitting the cache.
    /// </summary>
    public IReadOnlyList<BotSticker> ForPrompt { get; }

    public StickerCatalog(IEnumerable<Sticker> stickers)
    {
        var usable = stickers
            .Where(s => s.DeletedAt is null && !string.IsNullOrWhiteSpace(s.Description))
            .ToList();
        _byName = usable.ToDictionary(s => s.Name, s => new BotSticker(s.Id, s.Name, s.Description!.Trim()), StringComparer.Ordinal);
        ForPrompt = usable
            .OrderByDescending(s => s.UseCount)
            .ThenByDescending(s => s.CreatedAt)
            .ThenBy(s => s.Name, StringComparer.Ordinal)
            .Take(PromptLimit)
            .OrderBy(s => s.Name, StringComparer.Ordinal)
            .Select(s => _byName[s.Name])
            .ToList();
    }

    /// <summary>The sticker a bot named, forgiving case and spacing ("Cat Laughing" finds cat-laughing); null when there is none.</summary>
    public BotSticker? Find(string name) =>
        StickerRules.Slugify(name) is { } slug && _byName.TryGetValue(slug, out var sticker) ? sticker : null;

    public static async Task<StickerCatalog> LoadAsync(NeuralDamageDbContext db, CancellationToken ct = default) =>
        new(await db.Stickers.Where(s => s.DeletedAt == null && s.Description != null).AsNoTracking().ToListAsync(ct));
}
