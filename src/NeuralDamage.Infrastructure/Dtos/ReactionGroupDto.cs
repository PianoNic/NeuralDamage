namespace NeuralDamage.Infrastructure.Dtos;

/// <summary>
/// Reactions collapsed by emoji, which is what a chat actually renders. Names
/// are carried so the client can show who reacted without a second round trip.
/// </summary>
public record ReactionGroupDto(string Emoji, int Count, List<Guid> UserIds, List<Guid> BotIds, List<string> Names);

/// <summary>
/// Enough of the replied-to message to draw a quote without fetching it.
/// </summary>
public record ReplyInfoDto(Guid Id, string SenderName, string SenderType, string Content);
