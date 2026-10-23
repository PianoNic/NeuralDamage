namespace NeuralDamage.Infrastructure.Dtos;

/// <param name="Url">Where the picture loads from; only the bot's creator gets it.</param>
public record BotReferenceDto(Guid Id, string Url);

/// <summary>A bot's reference pictures and the looks written from them, for its creator.</summary>
/// <param name="Looks">What the bot looks like; null when nothing is written yet.</param>
public record BotReferencesDto(List<BotReferenceDto> References, string? Looks);
