using Mediator;
using Microsoft.EntityFrameworkCore;
using NeuralDamage.Infrastructure;
using NeuralDamage.Infrastructure.Dtos;
using NeuralDamage.Infrastructure.Mappers;
using NeuralDamage.Infrastructure.Models;
using NeuralDamage.Infrastructure.Services;

namespace NeuralDamage.Application.Commands;

/// <summary>Sets the names a person also goes by, so bots and people can @mention them by those.</summary>
public record UpdateNicknamesCommand(Guid UserId, string? Nicknames) : ICommand<Result<UserDto>>;

public class UpdateNicknamesHandler(NeuralDamageDbContext db, AppAdmins admins) : ICommandHandler<UpdateNicknamesCommand, Result<UserDto>>
{
    public async ValueTask<Result<UserDto>> Handle(UpdateNicknamesCommand request, CancellationToken cancellationToken)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == request.UserId, cancellationToken);
        if (user is null)
            return Result.Failure<UserDto>("User not found.");

        var (nicknames, error) = PeopleNames.Normalize(request.Nicknames);
        if (error is not null)
            return Result.Failure<UserDto>(error);

        user.Nicknames = nicknames;
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success(user.ToDto() with { IsAdmin = user.IsAdmin || admins.IsAdmin(user.Email) });
    }
}
