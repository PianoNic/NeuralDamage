using Mediator;
using Microsoft.EntityFrameworkCore;
using NeuralDamage.Infrastructure.Dtos;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure;
using NeuralDamage.Infrastructure.Mappers;
using NeuralDamage.Infrastructure.Models;
using NeuralDamage.Domain;

namespace NeuralDamage.Application.Commands;

/// <param name="IsPublic">Public bots are listed for everyone; a private bot belongs to <paramref name="ChatId"/> alone.</param>
/// <param name="ChatId">
/// The chat the new bot joins. Required for a private bot, which is bound to
/// it for good; optional for a public one. The creator must be a member.
/// </param>
public record CreateBotCommand(string Name, string ModelId, string SystemPrompt, string? Personality, double Temperature, string? Aliases, Guid CreatedById, bool IsPublic = true, Guid? ChatId = null) : ICommand<Result<BotDto>>;

public class CreateBotHandler(NeuralDamageDbContext db, IOpenRouterService openRouter, ModelPolicy modelPolicy, IChatNotificationService notifications) : ICommandHandler<CreateBotCommand, Result<BotDto>>
{
    public async ValueTask<Result<BotDto>> Handle(CreateBotCommand request, CancellationToken cancellationToken)
    {
        if (!request.IsPublic && request.ChatId is null)
            return Result<BotDto>.Failure("A private bot needs the chat it belongs to.");

        if (request.ChatId is { } chatId
            && !await db.ChatMembers.AnyAsync(cm => cm.ChatId == chatId && cm.UserId == request.CreatedById, cancellationToken))
            return Result<BotDto>.Failure("You are not a member of this chat.");

        // Names and nicknames only clash within a chat, so a bot made outside one can take any.
        if (request.ChatId is { } intoChatId
            && await BotNames.ClashInChatAsync(db, intoChatId, request.Name, request.Aliases, null, cancellationToken) is { } clash)
            return Result<BotDto>.Failure(clash);

        if (await modelPolicy.CheckModelAsync(openRouter, request.ModelId, cancellationToken) is { } refusal)
            return Result<BotDto>.Failure(refusal);

        var bot = new Bot
        {
            Name = request.Name,
            ModelId = request.ModelId,
            SystemPrompt = request.SystemPrompt,
            Personality = request.Personality,
            Temperature = request.Temperature,
            Aliases = request.Aliases,
            CreatedById = request.CreatedById,
            IsPublic = request.IsPublic,
            ChatId = request.IsPublic ? null : request.ChatId
        };
        db.Bots.Add(bot);

        ChatMember? member = null;
        if (request.ChatId is { } joinChatId)
        {
            member = new ChatMember { ChatId = joinChatId, BotId = bot.Id };
            db.ChatMembers.Add(member);
        }

        await db.SaveChangesAsync(cancellationToken);

        if (member is not null)
        {
            member.Bot = bot;
            await notifications.NotifyMemberAdded(member.ChatId, member.ToDto());
        }

        var dto = await db.Bots.Where(b => b.Id == bot.Id).SelectDto(DateTime.UtcNow).FirstAsync(cancellationToken);
        return Result<BotDto>.Success(dto);
    }
}
