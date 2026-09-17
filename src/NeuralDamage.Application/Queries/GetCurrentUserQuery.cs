using Mediator;
using Microsoft.EntityFrameworkCore;
using NeuralDamage.Infrastructure;
using NeuralDamage.Infrastructure.Dtos;
using NeuralDamage.Infrastructure.Mappers;
using NeuralDamage.Infrastructure.Models;
using NeuralDamage.Infrastructure.Services;

namespace NeuralDamage.Application.Queries;

public record GetCurrentUserQuery : IQuery<Result<UserDto>>;

public class GetCurrentUserHandler(NeuralDamageDbContext db, IUserService userService)
    : IQueryHandler<GetCurrentUserQuery, Result<UserDto>>
{
    public async ValueTask<Result<UserDto>> Handle(GetCurrentUserQuery request, CancellationToken cancellationToken)
    {
        var userId = await userService.GetCurrentUserIdAsync(cancellationToken);

        var user = await db.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);

        return user is null
            ? Result<UserDto>.Failure("User not found")
            : Result<UserDto>.Success(user.ToDto());
    }
}
