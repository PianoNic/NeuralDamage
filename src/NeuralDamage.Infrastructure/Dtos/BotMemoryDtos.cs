namespace NeuralDamage.Infrastructure.Dtos;

/// <summary>One thing a bot remembers.</summary>
/// <param name="Scope">Self, Person or Chat.</param>
/// <param name="Source">Chat, Feed, Story or Manual.</param>
/// <param name="SubjectName">Who a Person memory is about.</param>
/// <param name="ChatName">The chat it was learned in, when the reader is in it.</param>
/// <param name="StaysInChat">Learned in private, so only used in that chat.</param>
public record BotMemoryDto(
    Guid Id,
    Guid BotId,
    string BotName,
    string? BotAvatarUrl,
    string? BotModelId,
    string Scope,
    string Text,
    int Importance,
    string Source,
    bool Pinned,
    Guid? SubjectUserId,
    string? SubjectName,
    Guid? ChatId,
    string? ChatName,
    bool StaysInChat,
    DateTime CreatedAt,
    DateTime? LastUsedAt);

/// <summary>A bot's memory as its maker sees it: whether it is on, the notes it always keeps, and what it picked up.</summary>
/// <param name="Hidden">Memories from chats the reader is not in, about other people, which they don't get to read.</param>
public record BotMemoriesDto(bool Enabled, string? Notes, List<BotMemoryDto> Memories, int Hidden);
