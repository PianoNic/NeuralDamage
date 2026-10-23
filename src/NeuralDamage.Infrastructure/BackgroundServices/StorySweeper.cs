using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NeuralDamage.Infrastructure.Services.Attachments;

namespace NeuralDamage.Infrastructure.BackgroundServices;

/// <summary>
/// Stories disappear after 24 hours: the reads already leave them out, and
/// this deletes them and their PNGs for good, along with pictures uploaded
/// for stories that are older than any story could use.
/// </summary>
public class StorySweeper(IServiceScopeFactory scopes, ILogger<StorySweeper> logger, TimeProvider? time = null) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);

    /// <summary>An upload is only needed while its story is being made.</summary>
    public static readonly TimeSpan UploadLifetime = TimeSpan.FromDays(2);

    private readonly TimeProvider _time = time ?? TimeProvider.System;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, _time);
        try
        {
            do
            {
                try
                {
                    await SweepAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Story sweep failed");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <returns>How many stories and uploads were deleted.</returns>
    public async Task<(int Stories, int Uploads)> SweepAsync(CancellationToken ct = default)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NeuralDamageDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<IAttachmentStorage>();
        var now = _time.GetUtcNow().UtcDateTime;

        var expired = await db.Stories.Where(s => s.ExpiresAt <= now).ToListAsync(ct);
        var cutoff = now - UploadLifetime;
        var uploads = await db.StoryImages.Where(i => i.CreatedAt < cutoff).ToListAsync(ct);
        if (expired.Count == 0 && uploads.Count == 0)
            return (0, 0);

        db.Stories.RemoveRange(expired);
        db.StoryImages.RemoveRange(uploads);
        await db.SaveChangesAsync(ct);
        foreach (var id in expired.Select(s => s.Id).Concat(uploads.Select(i => i.Id)))
            storage.DeleteStoryFile(id);

        logger.LogInformation("Deleted {Stories} expired stories and {Uploads} old story uploads", expired.Count, uploads.Count);
        return (expired.Count, uploads.Count);
    }
}
