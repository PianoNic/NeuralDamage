using Microsoft.EntityFrameworkCore;
using NeuralDamage.Application.Interfaces;
using NeuralDamage.Domain;

namespace NeuralDamage.Infrastructure.Services;

public class UserSyncService(NeuralDamageDbContext dbContext) : IUserSyncService
{
    public async Task<User> SyncUserAsync(string externalId, string email, string displayName, string? avatarUrl = null, CancellationToken cancellationToken = default)
    {
        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.ExternalId == externalId, cancellationToken);

        if (user is null)
        {
            user = new User
            {
                ExternalId = externalId,
                Email = email,
                DisplayName = displayName,
                AvatarUrl = avatarUrl,
                LastLoginAt = DateTime.UtcNow
            };
            dbContext.Users.Add(user);
        }
        else
        {
            user.Email = email;
            user.DisplayName = displayName;
            user.AvatarUrl = avatarUrl;
            user.LastLoginAt = DateTime.UtcNow;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return user;
    }
}
