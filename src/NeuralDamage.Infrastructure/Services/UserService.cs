using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using NeuralDamage.Domain;
using Toamaisutaa.Abstractions;

namespace NeuralDamage.Infrastructure.Services
{
    /// <summary>
    /// Keeps the domain <see cref="User"/> row in step with the authenticated
    /// caller. Toamaisutaa owns authentication and hands us the identity through
    /// <see cref="ICurrentUser"/>; this table stays app-owned because chats,
    /// messages, bots and reactions all hang off it.
    /// </summary>
    public class UserService(
        ICurrentUser currentUser,
        IHttpContextAccessor httpContextAccessor,
        NeuralDamageDbContext dbContext) : IUserService
    {
        public async Task<bool> ExistsAsync(string externalId, CancellationToken cancellationToken = default)
        {
            return await dbContext.Users.AnyAsync(u => u.ExternalId == externalId, cancellationToken);
        }

        public async Task<Guid> GetCurrentUserIdAsync(CancellationToken cancellationToken = default)
        {
            var externalId = RequireSubject();

            var user = await dbContext.Users
                .SingleOrDefaultAsync(u => u.ExternalId == externalId, cancellationToken)
                ?? throw new UnauthorizedAccessException("User not found");

            return user.Id;
        }

        public async Task SyncCurrentUserAsync(CancellationToken cancellationToken = default)
        {
            var externalId = RequireSubject();

            var user = await dbContext.Users
                .SingleOrDefaultAsync(u => u.ExternalId == externalId, cancellationToken);

            // Toamaisutaa enriches the principal from the issuer's userinfo
            // endpoint, so these are present even when the access token omits
            // them. ICurrentUser itself only surfaces subject and name.
            var email = FindClaim(ClaimTypes.Email, "email") ?? $"{externalId}@unknown";
            var displayName = currentUser.Name ?? FindClaim(ClaimTypes.Name, "name") ?? email;
            var avatarUrl = FindClaim("picture");

            if (user is null)
            {
                dbContext.Users.Add(new User
                {
                    ExternalId = externalId,
                    Email = email,
                    DisplayName = displayName,
                    AvatarUrl = avatarUrl,
                    LastLoginAt = DateTime.UtcNow
                });
                await dbContext.SaveChangesAsync(cancellationToken);
                return;
            }

            user.Email = email;
            user.DisplayName = displayName;
            user.AvatarUrl = avatarUrl;
            user.LastLoginAt = DateTime.UtcNow;

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        private string RequireSubject() =>
            currentUser.Subject ?? throw new UnauthorizedAccessException("No authenticated user");

        private string? FindClaim(params string[] types)
        {
            var principal = httpContextAccessor.HttpContext?.User;
            if (principal is null)
                return null;

            return types
                .Select(principal.FindFirstValue)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        }
    }
}
