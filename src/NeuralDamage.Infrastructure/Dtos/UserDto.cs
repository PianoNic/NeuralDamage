namespace NeuralDamage.Infrastructure.Dtos;

/// <param name="IsAdmin">Whether the user may do anything any owner can. Only set on the signed-in user's own profile.</param>
/// <param name="Nicknames">Comma-separated other names that @mention the person.</param>
/// <param name="AllowBotMemory">Bots may remember things about the person.</param>
public record UserDto(Guid Id, string Email, string DisplayName, string? AvatarUrl, bool IsAdmin = false, string? Nicknames = null, bool AllowBotMemory = true);
