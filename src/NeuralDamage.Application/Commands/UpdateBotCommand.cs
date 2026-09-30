using Mediator;
using Microsoft.EntityFrameworkCore;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.BotDecision;
using NeuralDamage.Infrastructure;
using NeuralDamage.Infrastructure.Models;

namespace NeuralDamage.Application.Commands;

public record UpdateBotCommand(Guid BotId, Guid RequestingUserId, string? Name, string? ModelId, string? SystemPrompt, string? Personality, double? Temperature, string? AvatarUrl, string? Aliases, bool? IsActive) : ICommand<Result>;

public class UpdateBotHandler(NeuralDamageDbContext db, IOpenRouterService openRouter, ModelPriceCap priceCap) : ICommandHandler<UpdateBotCommand, Result>
{
    public async ValueTask<Result> Handle(UpdateBotCommand request, CancellationToken cancellationToken)
    {
        var bot = await db.Bots.FirstOrDefaultAsync(b => b.Id == request.BotId, cancellationToken);
        if (bot is null)
            return Result.Failure("Bot not found.");

        if (bot.CreatedById != request.RequestingUserId)
            return Result.Failure("Only the bot creator can update this bot.");

        // Only a change of model is checked, so lowering the cap does not lock
        // existing bots out of edits.
        if (request.ModelId is not null && request.ModelId != bot.ModelId
            && !await priceCap.AllowsModelAsync(openRouter, request.ModelId, cancellationToken))
            return Result.Failure($"Model '{request.ModelId}' exceeds the configured price cap.");

        if (request.Name is not null) bot.Name = request.Name;
        if (request.ModelId is not null) bot.ModelId = request.ModelId;
        if (request.SystemPrompt is not null) bot.SystemPrompt = request.SystemPrompt;
        if (request.Personality is not null) bot.Personality = request.Personality;
        if (request.Temperature is not null) bot.Temperature = request.Temperature.Value;
        if (request.AvatarUrl is not null) bot.AvatarUrl = request.AvatarUrl;
        if (request.Aliases is not null) bot.Aliases = request.Aliases;
        if (request.IsActive is not null) bot.IsActive = request.IsActive.Value;

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
