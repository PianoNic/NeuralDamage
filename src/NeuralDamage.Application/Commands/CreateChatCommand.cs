using Mediator;
using NeuralDamage.Application.Dtos;
using NeuralDamage.Application.Interfaces;
using NeuralDamage.Application.Mappers;
using NeuralDamage.Application.Models;
using NeuralDamage.Domain;
using NeuralDamage.Domain.Enums;

namespace NeuralDamage.Application.Commands;

public record CreateChatCommand(string Name, Guid CreatedById) : ICommand<Result<ChatDto>>;

public class CreateChatHandler(INeuralDamageDbContext db) : ICommandHandler<CreateChatCommand, Result<ChatDto>>
{
    public async ValueTask<Result<ChatDto>> Handle(CreateChatCommand request, CancellationToken cancellationToken)
    {
        var chat = new Chat { Name = request.Name, CreatedById = request.CreatedById };
        db.Chats.Add(chat);

        var member = new ChatMember { ChatId = chat.Id, UserId = request.CreatedById, Role = ChatMemberRole.Owner };
        db.ChatMembers.Add(member);

        await db.SaveChangesAsync(cancellationToken);

        return Result<ChatDto>.Success(chat.ToDto());
    }
}
