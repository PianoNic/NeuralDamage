namespace NeuralDamage.Application.Dtos;

public record BotSummaryDto(Guid Id, string Name, string? AvatarUrl, bool IsActive);
