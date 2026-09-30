using Mediator;
using Microsoft.EntityFrameworkCore;
using NeuralDamage.Infrastructure;
using NeuralDamage.Infrastructure.Dtos;
using NeuralDamage.Infrastructure.Mappers;
using NeuralDamage.Infrastructure.Models;

namespace NeuralDamage.Application.Queries;

/// <summary>
/// Users someone could invite into a chat: everyone but the caller, minus the
/// chat's current members when <see cref="ExcludeChatId"/> is given, optionally
/// narrowed by a name or email fragment.
/// </summary>
public record GetUsersQuery(Guid RequestingUserId, string? Search = null, Guid? ExcludeChatId = null, int Limit = 20) : IQuery<Result<List<UserDto>>>;

public class GetUsersHandler(NeuralDamageDbContext db) : IQueryHandler<GetUsersQuery, Result<List<UserDto>>>
{
    public async ValueTask<Result<List<UserDto>>> Handle(GetUsersQuery request, CancellationToken cancellationToken)
    {
        var query = db.Users.AsNoTracking().Where(u => u.Id != request.RequestingUserId);

        if (request.ExcludeChatId is { } chatId)
            query = query.Where(u => !db.ChatMembers.Any(cm => cm.ChatId == chatId && cm.UserId == u.Id));

        var search = request.Search?.Trim().ToLower();
        if (!string.IsNullOrEmpty(search))
            query = query.Where(u => u.DisplayName.ToLower().Contains(search) || u.Email.ToLower().Contains(search));

        var users = await query
            .OrderBy(u => u.DisplayName)
            .ThenBy(u => u.Email)
            .Take(Math.Clamp(request.Limit, 1, 100))
            .ToListAsync(cancellationToken);

        return Result<List<UserDto>>.Success(users.Select(u => u.ToDto()).ToList());
    }
}
