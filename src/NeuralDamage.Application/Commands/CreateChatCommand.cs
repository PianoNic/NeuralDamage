using Mediator;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.BotDecision;
using NeuralDamage.Infrastructure;
using NeuralDamage.Infrastructure.Dtos;
using NeuralDamage.Infrastructure.Mappers;
using NeuralDamage.Infrastructure.Models;
using NeuralDamage.Domain;
using NeuralDamage.Domain.Enums;

namespace NeuralDamage.Application.Commands;

public record CreateChatCommand(string Name, Guid CreatedById) : ICommand<Result<ChatDto>>;

public class CreateChatHandler(NeuralDamageDbContext db, IChatNotificationService notifications) : ICommandHandler<CreateChatCommand, Result<ChatDto>>
{
    public async ValueTask<Result<ChatDto>> Handle(CreateChatCommand request, CancellationToken cancellationToken)
    {
        var chat = new Chat { Name = request.Name, CreatedById = request.CreatedById };
        db.Chats.Add(chat);

        var member = new ChatMember { ChatId = chat.Id, UserId = request.CreatedById, Role = ChatMemberRole.Owner };
        db.ChatMembers.Add(member);

        await db.SaveChangesAsync(cancellationToken);

        var dto = chat.ToDto();
        await notifications.NotifyUserChatCreated(request.CreatedById, dto);
        return Result.Success(dto);
    }
}
