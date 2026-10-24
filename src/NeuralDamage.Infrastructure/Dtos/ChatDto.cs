namespace NeuralDamage.Infrastructure.Dtos;

/// <summary>
/// A chat in the inbox. The list also carries a preview: the last message and up to two members
/// other than the reader, bots first, for the avatars. Live events send the chat without it.
/// </summary>
/// <param name="IsDirect">Just the reader and one bot or person; shown as that one.</param>
/// <param name="AvatarUrl">A group chat's own picture.</param>
public record ChatDto(
    Guid Id,
    string Name,
    Guid CreatedById,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    bool IsDirect = false,
    ChatPreviewDto? Preview = null,
    string? AvatarUrl = null);

public record ChatPreviewDto(
    string? LastMessage,
    string? LastSenderName,
    DateTime? LastMessageAt,
    int MemberCount,
    List<ChatPreviewMemberDto> Members);

public record ChatPreviewMemberDto(string Name, string? AvatarUrl, string? ModelId, bool IsBot);
