using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.BotDecision;

namespace NeuralDamage.Infrastructure.BackgroundServices;

public record BotResponseRequest(Guid ChatId, Guid MessageId, int Depth = 0);

public class BotResponseQueue : IBotResponseQueue
{
    private readonly Channel<BotResponseRequest> _channel = Channel.CreateUnbounded<BotResponseRequest>();

    public ChannelReader<BotResponseRequest> Reader => _channel.Reader;

    public ValueTask EnqueueAsync(Guid chatId, Guid messageId, CancellationToken ct = default)
        => EnqueueAsync(chatId, messageId, 0, ct);

    public async ValueTask EnqueueAsync(Guid chatId, Guid messageId, int depth, CancellationToken ct = default)
    {
        await _channel.Writer.WriteAsync(new BotResponseRequest(chatId, messageId, depth), ct);
    }
}

/// <summary>
/// Hands each chat's messages to a worker of its own. Within a chat, messages
/// are answered one at a time and in order; across chats they run
/// concurrently, so a slow model in one chat no longer stalls every other.
/// </summary>
public class BotResponseBackgroundService(BotResponseQueue queue, IBotResponseOrchestrator orchestrator, ILogger<BotResponseBackgroundService> logger) : BackgroundService
{
    // Only this loop adds workers, so a worker is never created twice. An idle
    // worker is a parked ReadAllAsync, so keeping one per chat is cheap.
    private readonly ConcurrentDictionary<Guid, Channel<BotResponseRequest>> _chats = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var request in queue.Reader.ReadAllAsync(stoppingToken))
        {
            var chat = _chats.GetOrAdd(request.ChatId, chatId =>
            {
                var channel = Channel.CreateUnbounded<BotResponseRequest>(new UnboundedChannelOptions { SingleReader = true });
                _ = Task.Run(() => RunChatAsync(channel.Reader, stoppingToken), stoppingToken);
                return channel;
            });
            chat.Writer.TryWrite(request);
        }
    }

    private async Task RunChatAsync(ChannelReader<BotResponseRequest> reader, CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var request in reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await orchestrator.ProcessMessageAsync(request.ChatId, request.MessageId, request.Depth, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Error processing bot response for chat {ChatId}, message {MessageId}", request.ChatId, request.MessageId);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }
}
