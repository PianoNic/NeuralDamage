namespace NeuralDamage.Infrastructure.Dtos;

/// <param name="IsAdmin">Whether the user may do anything any owner can. Only set on the signed-in user's own profile.</param>
public record UserDto(Guid Id, string Email, string DisplayName, string? AvatarUrl, bool IsAdmin = false);
