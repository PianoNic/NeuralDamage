namespace NeuralDamage.Infrastructure.Dtos.Requests;

/// <summary>An empty <paramref name="Description"/> hides the sticker from the bots.</summary>
public record UpdateStickerRequest(string Name, string? Description);
