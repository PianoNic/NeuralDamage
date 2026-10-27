using Mediator;
using Microsoft.EntityFrameworkCore;
using NeuralDamage.Infrastructure;
using NeuralDamage.Infrastructure.Dtos;
using NeuralDamage.Infrastructure.Mappers;
using NeuralDamage.Infrastructure.Models;
using NeuralDamage.Infrastructure.Services;

namespace NeuralDamage.Application.Commands;

/// <summary>Whether bots in the person's group chats may decide to text them directly.</summary>
public record AllowBotDirectMessagesCommand(Guid UserId, bool Allowed) : ICommand<Result<UserDto>>;

public class AllowBotDirectMessagesHandler(NeuralDamageDbContext db, AppAdmins admins) : ICommandHandler<AllowBotDirectMessagesCommand, Result<UserDto>>
{
    public async ValueTask<Result<UserDto>> Handle(AllowBotDirectMessagesCommand request, CancellationToken cancellationToken)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == request.UserId, cancellationToken);
        if (user is null)
            return Result.Failure<UserDto>("User not found.");

        user.AllowBotDirectMessages = request.Allowed;
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success(user.ToDto() with { IsAdmin = user.IsAdmin || admins.IsAdmin(user.Email) });
    }
}
