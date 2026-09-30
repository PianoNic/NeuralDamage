namespace NeuralDamage.Infrastructure.Dtos.Requests;

/// <summary><paramref name="AttachmentIds"/> are images uploaded to the chat beforehand.</summary>
public record SendMessageRequest(string Content, Guid? ReplyToId = null, List<Guid>? AttachmentIds = null);
