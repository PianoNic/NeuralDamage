namespace NeuralDamage.Infrastructure.Dtos.Requests;

public record CreateBotRequest(string Name, string ModelId, string SystemPrompt, string? Personality = null, double Temperature = 0.7, string? AvatarUrl = null, string? Aliases = null);
