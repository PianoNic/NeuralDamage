using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace NeuralDamage.Infrastructure.Extensions;

/// <summary>
/// Per-user limits on the endpoints that cost money or fill a chat: every
/// message can wake paid bots, every upload is described by a paid model.
/// Configured under <c>RateLimits:&lt;Policy&gt;:PermitLimit</c> and
/// <c>:WindowSeconds</c>; a permit limit of 0 turns a policy off.
/// </summary>
public static class RateLimitExtensions
{
    public const string Messages = "Messages";
    public const string Uploads = "Uploads";
    public const string BotCreation = "BotCreation";

    private static readonly Dictionary<string, (int PermitLimit, int WindowSeconds, string Text)> Defaults = new()
    {
        [Messages] = (20, 60, "You're sending messages too fast. Wait a moment and try again."),
        [Uploads] = (20, 60, "You're uploading images too fast. Wait a moment and try again."),
        [BotCreation] = (10, 600, "You're creating bots too fast. Wait a few minutes and try again."),
    };

    public static IServiceCollection AddChatRateLimits(this IServiceCollection services, IConfiguration configuration)
    {
        return services.AddRateLimiter(options =>
        {
            foreach (var (policy, defaults) in Defaults)
            {
                var section = configuration.GetSection($"RateLimits:{policy}");
                var permitLimit = section.GetValue("PermitLimit", defaults.PermitLimit);
                var window = TimeSpan.FromSeconds(section.GetValue("WindowSeconds", defaults.WindowSeconds));

                options.AddPolicy(policy, context => permitLimit <= 0
                    ? RateLimitPartition.GetNoLimiter(policy)
                    // Fixed rather than sliding: its lease says when the window
                    // reopens, which goes back as Retry-After.
                    : RateLimitPartition.GetFixedWindowLimiter(PartitionKey(context), _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = permitLimit,
                        Window = window,
                        QueueLimit = 0,
                    }));
            }

            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, ct) =>
            {
                var policy = context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;
                var text = policy is not null && Defaults.TryGetValue(policy, out var d) ? d.Text : "Too many requests. Wait a moment and try again.";
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    context.HttpContext.Response.Headers.RetryAfter = Math.Ceiling(retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                // Plain text, like the API's other refusals, so the client shows it as is.
                context.HttpContext.Response.ContentType = "text/plain; charset=utf-8";
                await context.HttpContext.Response.WriteAsync(text, ct);
            };
        });
    }

    /// <summary>The signed-in user; the address only for a request without one, which auth rejects anyway.</summary>
    private static string PartitionKey(HttpContext context) =>
        context.User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? context.User.FindFirstValue("sub")
        ?? $"ip:{context.Connection.RemoteIpAddress}";
}
