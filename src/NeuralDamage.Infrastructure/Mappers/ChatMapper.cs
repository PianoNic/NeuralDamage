using NeuralDamage.Infrastructure.Dtos;
using NeuralDamage.Domain;

namespace NeuralDamage.Infrastructure.Mappers;

public static class ChatMapper
{
    public static ChatDto ToDto(this Chat chat) => new(chat.Id, chat.Name, chat.CreatedById, chat.CreatedAt, chat.UpdatedAt);

    public static ChatDetailDto ToDetailDto(this Chat chat, List<ChatMemberDto> members) => new(chat.Id, chat.Name, chat.CreatedById, chat.CreatedAt, chat.UpdatedAt, members);
}
