using NeuralDamage.Application.Dtos;
using NeuralDamage.Domain;

namespace NeuralDamage.Application.Mappers;

public static class ChatMapper
{
    public static ChatDto ToDto(this Chat chat) => new(chat.Id, chat.Name, chat.CreatedById, chat.CreatedAt, chat.UpdatedAt);

    public static ChatDetailDto ToDetailDto(this Chat chat, List<ChatMemberDto> members) => new(chat.Id, chat.Name, chat.CreatedById, chat.CreatedAt, chat.UpdatedAt, members);
}
