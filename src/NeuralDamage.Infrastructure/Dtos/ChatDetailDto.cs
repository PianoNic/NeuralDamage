namespace NeuralDamage.Infrastructure.Dtos;

public record ChatDetailDto(Guid Id, string Name, Guid CreatedById, DateTime CreatedAt, DateTime UpdatedAt, List<ChatMemberDto> Members);
