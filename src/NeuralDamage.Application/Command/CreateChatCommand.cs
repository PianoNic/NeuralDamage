using Mediator;
using NeuralDamage.Application.Interfaces;
using NeuralDamage.Application.Mappers;
using NeuralDamage.Application.Models;
using NeuralDamage.Domain;
using NeuralDamage.Domain.Enums;

namespace NeuralDamage.Application.Command;

public record CreateChatCommand(string Name, Guid CreatedById) : ICommand<Result>;

public class CreateChatHandler(INeuralDamageDbContext db, IChatNotificationService notifications) : ICommandHandler<CreateChatCommand, Result>
{
    public async ValueTask<Result> Handle(CreateChatCommand request, CancellationToken cancellationToken)
    {
        var chat = new Chat { Name = request.Name, CreatedById = request.CreatedById };
        db.Chats.Add(chat);

        var member = new ChatMember { ChatId = chat.Id, UserId = request.CreatedById, Role = ChatMemberRole.Owner };
        db.ChatMembers.Add(member);

        await db.SaveChangesAsync(cancellationToken);

        await notifications.NotifyUserChatCreated(request.CreatedById, chat.ToDto());
        return Result.Success();
    }
}
