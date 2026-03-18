using Microsoft.EntityFrameworkCore;
using NeuralDamage.Application.Interfaces;

namespace NeuralDamage.API.Services;

public class UserResolverService(ICurrentUserService currentUserService, INeuralDamageDbContext db) : IUserResolverService
{
    private Guid? _cachedUserId;

    public async Task<Guid> GetCurrentUserIdAsync(CancellationToken ct = default)
    {
        if (_cachedUserId.HasValue)
            return _cachedUserId.Value;

        var externalId = currentUserService.ExternalId
            ?? throw new UnauthorizedAccessException("User is not authenticated.");

        var userId = await db.Users
            .Where(u => u.ExternalId == externalId)
            .Select(u => u.Id)
            .FirstOrDefaultAsync(ct);

        if (userId == Guid.Empty)
            throw new KeyNotFoundException("User not found.");

        _cachedUserId = userId;
        return userId;
    }
}
