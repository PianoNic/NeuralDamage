using Mediator;
using Microsoft.EntityFrameworkCore;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.BotDecision;
using NeuralDamage.Infrastructure;
using NeuralDamage.Infrastructure.Models;

namespace NeuralDamage.Application.Commands;

public record UpdateBotCommand(Guid BotId, Guid RequestingUserId, string? Name, string? ModelId, string? SystemPrompt, string? Personality, double? Temperature, string? Aliases, bool? IsActive) : ICommand<Result>;

public class UpdateBotHandler(NeuralDamageDbContext db, IOpenRouterService openRouter, ModelPolicy modelPolicy, IChatBotState botState) : ICommandHandler<UpdateBotCommand, Result>
{
    public async ValueTask<Result> Handle(UpdateBotCommand request, CancellationToken cancellationToken)
    {
        var bot = await db.Bots.FirstOrDefaultAsync(b => b.Id == request.BotId, cancellationToken);
        if (bot is null)
            return Result.Failure("Bot not found.");

        if (bot.CreatedById != request.RequestingUserId)
            return Result.Failure("Only the bot creator can update this bot.");

        // Only a change of model is checked, so tightening the policy does not
        // lock existing bots out of edits.
        if (request.ModelId is not null && request.ModelId != bot.ModelId
            && await modelPolicy.CheckModelAsync(openRouter, request.ModelId, cancellationToken) is { } refusal)
            return Result.Failure(refusal);

        var renamed = request.Name is not null && !string.Equals(request.Name, bot.Name, StringComparison.OrdinalIgnoreCase);
        var renicknamed = request.Aliases is not null && request.Aliases != bot.Aliases;
        if (renamed || renicknamed)
        {
            var chatIds = await db.ChatMembers.Where(cm => cm.BotId == bot.Id).Select(cm => cm.ChatId).ToListAsync(cancellationToken);
            var where = chatIds.Count == 1 ? "This chat" : "A chat this bot is in";
            foreach (var chatId in chatIds)
                if (await BotNames.ClashInChatAsync(db, chatId, request.Name ?? bot.Name, request.Aliases ?? bot.Aliases, bot.Id, cancellationToken, where) is { } clash)
                    return Result.Failure(clash);
        }

        if (request.Name is not null) bot.Name = request.Name;
        if (request.ModelId is not null && request.ModelId != bot.ModelId)
        {
            bot.ModelId = request.ModelId;
            // A fixed bot starts fresh: if the new model breaks too, the chat hears about it again.
            botState.ClearModelNotices(bot.Id);
        }
        if (request.SystemPrompt is not null) bot.SystemPrompt = request.SystemPrompt;
        if (request.Personality is not null) bot.Personality = request.Personality;
        if (request.Temperature is not null) bot.Temperature = request.Temperature.Value;
        if (request.Aliases is not null) bot.Aliases = request.Aliases;
        if (request.IsActive is not null) bot.IsActive = request.IsActive.Value;

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
