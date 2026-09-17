using Microsoft.AspNetCore.Authentication.JwtBearer;
using NeuralDamage.Infrastructure.Services;
using System.Security.Claims;

namespace NeuralDamage.API.Extensions
{
    public static class AuthenticationExtensions
    {
        /// <summary>
        /// Mirrors the authenticated caller into the domain Users table on first
        /// sight. Toamaisutaa owns authentication, but this app owns its own user
        /// row because chats, messages, bots and reactions are keyed to it.
        /// </summary>
        /// <remarks>
        /// Hangs off JwtBearerOptions rather than the AuthenticationBuilder, since
        /// AddToamaisutaaBearer configures the scheme itself and does not hand one
        /// back to chain from.
        /// </remarks>
        public static IServiceCollection AddUserSync(this IServiceCollection services)
        {
            services.PostConfigure<JwtBearerOptions>(
                JwtBearerDefaults.AuthenticationScheme,
                options =>
                {
                    options.Events ??= new JwtBearerEvents();
                    var previous = options.Events.OnTokenValidated;

                    options.Events.OnTokenValidated = async context =>
                    {
                        // Toamaisutaa enriches the principal from the userinfo
                        // endpoint in its own handler, so run it first.
                        if (previous is not null)
                            await previous(context);

                        if (context.Principal?.Identity is not ClaimsIdentity identity)
                            return;

                        context.HttpContext.User = context.Principal;

                        var externalId = identity.FindFirst("sub")?.Value ?? identity.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                        if (string.IsNullOrEmpty(externalId))
                            return;

                        var userService = context.HttpContext.RequestServices.GetRequiredService<IUserService>();

                        if (await userService.ExistsAsync(externalId, context.HttpContext.RequestAborted))
                            return;

                        await userService.SyncCurrentUserAsync(context.HttpContext.RequestAborted);
                    };
                });

            return services;
        }
    }
}
