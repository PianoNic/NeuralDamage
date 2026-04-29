namespace NeuralDamage.Infrastructure.Dtos;

public record BotDto(Guid Id, string Name, string ModelId, string SystemPrompt, string? Personality, double Temperature, string? AvatarUrl, string? Aliases, Guid CreatedById, bool IsActive, DateTime CreatedAt);
