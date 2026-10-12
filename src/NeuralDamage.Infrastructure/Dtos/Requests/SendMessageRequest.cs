namespace NeuralDamage.Infrastructure.Dtos.Requests;

/// <summary>
/// <paramref name="AttachmentIds"/> are images uploaded to the chat beforehand.
/// A <paramref name="StickerId"/> sends that sticker, with no text.
/// </summary>
public record SendMessageRequest(string Content, Guid? ReplyToId = null, List<Guid>? AttachmentIds = null, Guid? StickerId = null);
