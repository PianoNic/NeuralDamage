namespace NeuralDamage.Application.Dtos;

public record UserDto(Guid Id, string Email, string DisplayName, string? AvatarUrl);
