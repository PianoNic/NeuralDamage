using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.BotDecision;

namespace NeuralDamage.Infrastructure.BackgroundServices;

public record BotResponseRequest(Guid ChatId, Guid MessageId);

public class BotResponseQueue : IBotResponseQueue
{
    private readonly Channel<BotResponseRequest> _channel = Channel.CreateUnbounded<BotResponseRequest>();

    public ChannelReader<BotResponseRequest> Reader => _channel.Reader;

    public async ValueTask EnqueueAsync(Guid chatId, Guid messageId, CancellationToken ct = default)
    {
        await _channel.Writer.WriteAsync(new BotResponseRequest(chatId, messageId), ct);
    }
}

public class BotResponseBackgroundService(BotResponseQueue queue, IBotResponseOrchestrator orchestrator, ILogger<BotResponseBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var request in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await orchestrator.ProcessMessageAsync(request.ChatId, request.MessageId, stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing bot response for chat {ChatId}, message {MessageId}", request.ChatId, request.MessageId);
            }
        }
    }
}
