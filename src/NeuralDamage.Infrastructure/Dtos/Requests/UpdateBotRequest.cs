namespace NeuralDamage.Infrastructure.Dtos.Requests;

public record UpdateBotRequest(string? Name = null, string? ModelId = null, string? SystemPrompt = null, string? Personality = null, double? Temperature = null, string? AvatarUrl = null, string? Aliases = null, bool? IsActive = null);
