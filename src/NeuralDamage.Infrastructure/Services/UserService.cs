using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
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
        NeuralDamageDbContext dbContext,
        DbContextOptions<NeuralDamageDbContext> dbOptions,
        ILogger<UserService> logger) : IUserService
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

        public async Task<bool> NeedsSyncAsync(string externalId, CancellationToken cancellationToken = default)
        {
            var lastLogin = await dbContext.Users
                .Where(u => u.ExternalId == externalId)
                .Select(u => u.LastLoginAt)
                .FirstOrDefaultAsync(cancellationToken);

            return lastLogin is null || DateTime.UtcNow - lastLogin.Value > TimeSpan.FromMinutes(15);
        }

        public async Task<Guid?> GetUserIdByExternalIdAsync(string externalId, CancellationToken cancellationToken = default)
        {
            var user = await dbContext.Users
                .AsNoTracking()
                .SingleOrDefaultAsync(u => u.ExternalId == externalId, cancellationToken);

            return user?.Id;
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
            // Prefer the handle the provider exposes over the legal/full name.
            var displayName = FindClaim("preferred_username", "nickname", "username")
                ?? currentUser.Name
                ?? FindClaim(ClaimTypes.Name, "name")
                ?? email;
            var avatarUrl = FindClaim("picture");

            if (user is null)
            {
                var created = new User
                {
                    ExternalId = externalId,
                    Email = email,
                    DisplayName = displayName,
                    AvatarUrl = avatarUrl,
                    LastLoginAt = DateTime.UtcNow
                };
                await CreateAsync(created, cancellationToken);
                return;
            }

            user.Email = email;
            user.DisplayName = displayName;
            user.AvatarUrl = avatarUrl;
            user.LastLoginAt = DateTime.UtcNow;

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        /// <summary>
        /// Inserts a new user. Right after sign-in the web app fires several
        /// requests at once, and each one finds no row and tries to create it.
        /// One insert wins; the others lose on the unique index and must not
        /// fail the request - nor log an error for what is expected. The insert
        /// runs in a context of its own whose failed-save events are logged at
        /// debug; anything else still surfaces as the exception it is.
        /// </summary>
        private async Task CreateAsync(User created, CancellationToken cancellationToken)
        {
            var quietOptions = new DbContextOptionsBuilder<NeuralDamageDbContext>(dbOptions)
                .ConfigureWarnings(w => w.Log(
                    (CoreEventId.SaveChangesFailed, LogLevel.Debug),
                    (RelationalEventId.CommandError, LogLevel.Debug)))
                .Options;
            await using var quiet = new NeuralDamageDbContext(quietOptions);
            quiet.Users.Add(created);

            try
            {
                await quiet.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex)
            {
                if (!await ExistsAsync(created.ExternalId, cancellationToken))
                    throw;
                logger.LogDebug(ex, "User {ExternalId} was created by a concurrent request", created.ExternalId);
            }
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
