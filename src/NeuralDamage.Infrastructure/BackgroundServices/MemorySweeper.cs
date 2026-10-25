using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NeuralDamage.Infrastructure.Services.Memory;

namespace NeuralDamage.Infrastructure.BackgroundServices;

/// <summary>
/// Once a day, keeps each bot's memory to <see cref="MemoryOptions.MaxPerBot"/> by dropping what
/// is least useful, never pinned or written-in memories. No model call: what a bot keeps is
/// already de-duplicated when it is written.
/// </summary>
public class MemorySweeper(IServiceScopeFactory scopes, ILogger<MemorySweeper> logger, IOptions<MemoryOptions>? options = null, TimeProvider? time = null) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    private readonly MemoryOptions _options = options?.Value ?? new();
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
                    logger.LogWarning(ex, "Memory sweep failed");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <returns>How many memories were dropped.</returns>
    public async Task<int> SweepAsync(CancellationToken ct = default)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NeuralDamageDbContext>();
        var crowded = await db.BotMemories
            .GroupBy(m => m.BotId)
            .Where(g => g.Count() > _options.MaxPerBot)
            .Select(g => g.Key)
            .ToListAsync(ct);

        var dropped = 0;
        foreach (var botId in crowded)
            dropped += await BotMemories.PruneAsync(db, botId, _options, ct);
        if (dropped > 0)
            logger.LogInformation("Memory sweep dropped {Count} memories", dropped);
        return dropped;
    }
}
