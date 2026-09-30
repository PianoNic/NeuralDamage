using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NeuralDamage.Infrastructure.Services.Attachments;

namespace NeuralDamage.Infrastructure.BackgroundServices;

/// <summary>
/// Deletes images that were uploaded but never sent: someone picked a picture
/// and then removed it, left the chat or closed the tab. Without this they
/// stay on disk until the whole chat is cleared or deleted.
/// </summary>
public class UnsentAttachmentSweeper(
    IServiceScopeFactory scopeFactory,
    IAttachmentStorage storage,
    AttachmentOptions options,
    ILogger<UnsentAttachmentSweeper> logger) : BackgroundService
{
    /// <summary>How often to look, never less often than the age limit itself.</summary>
    private TimeSpan Interval => options.UnsentMaxAge < TimeSpan.FromMinutes(10) ? options.UnsentMaxAge : TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        try
        {
            do
            {
                try
                {
                    await SweepAsync(DateTime.UtcNow, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Sweeping unsent images failed; trying again in {Interval}", Interval);
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }

    /// <summary>Deletes every upload older than the limit that no message took, and returns how many.</summary>
    public async Task<int> SweepAsync(DateTime now, CancellationToken ct = default)
    {
        var cutoff = now - options.UnsentMaxAge;
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NeuralDamageDbContext>();

        var stale = await db.Attachments
            .Where(a => a.MessageId == null && a.CreatedAt < cutoff)
            .ToListAsync(ct);
        if (stale.Count == 0)
            return 0;

        // Rows first, so a message sent meanwhile cannot pick up an upload whose file is already gone.
        db.Attachments.RemoveRange(stale);
        await db.SaveChangesAsync(ct);
        foreach (var attachment in stale)
            storage.Delete(attachment.ChatId, attachment.Id);

        logger.LogInformation("Swept {Count} image(s) uploaded but never sent", stale.Count);
        return stale.Count;
    }
}
