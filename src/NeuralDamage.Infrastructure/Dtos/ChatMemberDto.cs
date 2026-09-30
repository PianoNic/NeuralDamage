namespace NeuralDamage.Infrastructure.Dtos;

/// <param name="IsMuted">Whether the bot is muted in this chat; always false for people.</param>
/// <param name="ModelStatus">For a bot: available, missing or notAllowed (see <see cref="BotDto"/>). Null for people.</param>
/// <param name="ModelStatusReason">Why a bot's model cannot be used; null otherwise.</param>
public record ChatMemberDto(Guid Id, Guid ChatId, Guid? UserId, Guid? BotId, string Role, DateTime JoinedAt, UserDto? User, BotSummaryDto? Bot, bool IsMuted = false, string? ModelStatus = null, string? ModelStatusReason = null);
