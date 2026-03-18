namespace NeuralDamage.Domain;

public class Bot : BaseEntity
{
    public required string Name { get; set; }
    public required string ModelId { get; set; }
    public string SystemPrompt { get; set; } = string.Empty;
    public string? Personality { get; set; }
    public double Temperature { get; set; } = 0.7;
    public string? AvatarUrl { get; set; }
    public string? Aliases { get; set; }
    public bool IsActive { get; set; } = true;

    public required Guid CreatedById { get; init; }
    public User CreatedBy { get; set; } = null!;

    public ICollection<ChatMember> ChatMembers { get; set; } = [];
    public ICollection<Message> Messages { get; set; } = [];
    public ICollection<Reaction> Reactions { get; set; } = [];
}
