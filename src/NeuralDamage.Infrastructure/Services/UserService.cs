using Microsoft.EntityFrameworkCore;
using NeuralDamage.Domain;

namespace NeuralDamage.Infrastructure.Services
{
    public class UserService(IOidcService oidcService, NeuralDamageDbContext dbContext) : IUserService
    {
        public async Task<bool> ExistsAsync(string externalId, CancellationToken cancellationToken = default)
        {
            return await dbContext.Users.AnyAsync(u => u.ExternalId == externalId, cancellationToken);
        }

        public async Task<Guid> GetCurrentUserIdAsync(CancellationToken cancellationToken = default)
        {
            var oidcUser = await oidcService.GetCurrentUserAsync(cancellationToken)
                ?? throw new UnauthorizedAccessException("No authenticated user");

            var user = await dbContext.Users
                .SingleOrDefaultAsync(u => u.ExternalId == oidcUser.ExternalId, cancellationToken)
                ?? throw new UnauthorizedAccessException("User not found");

            return user.Id;
        }

        public async Task SyncCurrentUserAsync(CancellationToken cancellationToken = default)
        {
            var oidcUser = await oidcService.GetCurrentUserAsync(cancellationToken)
                ?? throw new UnauthorizedAccessException("No authenticated user");

            var user = await dbContext.Users
                .SingleOrDefaultAsync(u => u.ExternalId == oidcUser.ExternalId, cancellationToken);

            var email = oidcUser.Email ?? $"{oidcUser.ExternalId}@unknown";
            var displayName = oidcUser.DisplayName ?? email;

            if (user is null)
            {
                user = new User
                {
                    ExternalId = oidcUser.ExternalId,
                    Email = email,
                    DisplayName = displayName,
                    AvatarUrl = oidcUser.AvatarUrl,
                    LastLoginAt = DateTime.UtcNow
                };
                dbContext.Users.Add(user);
                await dbContext.SaveChangesAsync(cancellationToken);
                return;
            }

            user.Email = email;
            user.DisplayName = displayName;
            user.AvatarUrl = oidcUser.AvatarUrl;
            user.LastLoginAt = DateTime.UtcNow;

            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
