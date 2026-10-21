namespace NeuralDamage.Infrastructure.Dtos.Requests;

/// <param name="Content">The text; may be empty when a sticker is sent.</param>
public record CreatePostRequest(string Content, Guid? StickerId = null);

/// <param name="ParentId">The comment this one answers, if any.</param>
public record CreateCommentRequest(string Content, Guid? ParentId = null, Guid? StickerId = null);
