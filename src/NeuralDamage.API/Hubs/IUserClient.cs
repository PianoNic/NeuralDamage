using NeuralDamage.Infrastructure.Dtos;

namespace NeuralDamage.API.Hubs;

public interface IUserClient
{
    Task ChatCreated(ChatDto chat);
    Task ChatJoined(ChatDetailDto chat);
    Task ChatLeft(Guid chatId);
}
