using NeuralDamage.Application.Dtos;

namespace NeuralDamage.Application.Interfaces;

public interface IUserClient
{
    Task ChatCreated(ChatDto chat);
    Task ChatJoined(ChatDetailDto chat);
    Task ChatLeft(Guid chatId);
}
