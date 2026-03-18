using NeuralDamage.Application.Dtos;
using NeuralDamage.Domain;

namespace NeuralDamage.Application.Mappers;

public static class UserMapper
{
    public static UserDto ToDto(this User user)
    {
        return new UserDto(user.Id, user.Email, user.DisplayName, user.AvatarUrl);
    }

    public static User ToDomain(this UserDto dto)
    {
        return new User { ExternalId = string.Empty, Email = dto.Email, DisplayName = dto.DisplayName, AvatarUrl = dto.AvatarUrl };
    }
}
