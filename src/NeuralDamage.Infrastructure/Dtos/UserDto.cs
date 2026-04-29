namespace NeuralDamage.Infrastructure.Dtos;

public record UserDto(Guid Id, string Email, string DisplayName, string? AvatarUrl);
