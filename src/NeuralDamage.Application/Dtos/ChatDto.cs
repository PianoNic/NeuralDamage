namespace NeuralDamage.Application.Dtos;

public record ChatDto(Guid Id, string Name, Guid CreatedById, DateTime CreatedAt, DateTime UpdatedAt);
