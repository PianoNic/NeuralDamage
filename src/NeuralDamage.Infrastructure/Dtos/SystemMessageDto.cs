namespace NeuralDamage.Infrastructure.Dtos;

/// <summary>
/// A notice for everyone in the chat (command feedback, a bot failing). It is
/// broadcast once and never saved, so it does not appear in message history.
/// </summary>
public record SystemMessageDto(Guid ChatId, string Content, DateTimeOffset Timestamp);
