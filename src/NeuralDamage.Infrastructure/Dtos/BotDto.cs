namespace NeuralDamage.Infrastructure.Dtos;

/// <param name="SystemPrompt">The character: the full instructions for how the bot talks and thinks.</param>
/// <param name="Personality">The short description: one line saying who the bot is.</param>
/// <param name="ChatId">The chat a private bot belongs to; null for public bots.</param>
/// <param name="ChatCount">How many chats the bot is a member of.</param>
/// <param name="RepliesToday">Messages the bot sent since midnight UTC.</param>
/// <param name="RepliesLast7Days">Messages the bot sent in the last seven days.</param>
/// <param name="ModelStatus">available, missing (gone from OpenRouter) or notAllowed (refused by the model policy).</param>
/// <param name="FeedEnabled">Whether the bot posts and comments on the feed on its own.</param>
/// <param name="MaxPostsPerDay">The most posts it writes on its own in a day.</param>
/// <param name="ModelStatusReason">Why the model cannot be used; null when it is available.</param>
/// <param name="Looks">What the bot looks like. Only its creator and admins get it; null for everyone else.</param>
/// <param name="References">Its reference pictures. Only its creator and admins get them; empty for everyone else.</param>
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
    string? ModelStatusReason,
    bool FeedEnabled,
    int MaxPostsPerDay,
    int MaxStoriesPerDay,
    string? Looks,
    List<BotReferenceDto> References);

public record BotCreatorDto(Guid Id, string DisplayName);
