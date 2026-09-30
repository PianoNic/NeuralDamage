namespace NeuralDamage.Infrastructure.Dtos;

/// <param name="ChatId">The chat a private bot belongs to; null for public bots.</param>
/// <param name="ChatCount">How many chats the bot is a member of.</param>
/// <param name="RepliesToday">Messages the bot sent since midnight UTC.</param>
/// <param name="RepliesLast7Days">Messages the bot sent in the last seven days.</param>
/// <param name="ModelStatus">available, missing (gone from OpenRouter) or notAllowed (refused by the model policy).</param>
/// <param name="ModelStatusReason">Why the model cannot be used; null when it is available.</param>
public record BotDto(
    Guid Id,
    string Name,
    string ModelId,
    string SystemPrompt,
    string? Personality,
    double Temperature,
    string? AvatarUrl,
    string? Aliases,
    Guid CreatedById,
    bool IsActive,
    DateTime CreatedAt,
    bool IsPublic,
    Guid? ChatId,
    BotCreatorDto CreatedBy,
    int ChatCount,
    int RepliesToday,
    int RepliesLast7Days,
    string ModelStatus,
    string? ModelStatusReason);

public record BotCreatorDto(Guid Id, string DisplayName);
