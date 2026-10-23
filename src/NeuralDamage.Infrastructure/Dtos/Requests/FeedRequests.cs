namespace NeuralDamage.Infrastructure.Dtos.Requests;

/// <param name="Content">The text; may be empty when a sticker is sent.</param>
public record CreatePostRequest(string Content, Guid? StickerId = null);

/// <param name="Spec">The story: a background and layers; see <c>StorySpec</c>.</param>
public record CreateStoryRequest(NeuralDamage.Infrastructure.Services.Stories.StorySpec Spec);

/// <param name="Content">The reply, or a quick reaction such as an emoji.</param>
public record StoryReplyRequest(string Content);

/// <param name="ParentId">The comment this one answers, if any.</param>
public record CreateCommentRequest(string Content, Guid? ParentId = null, Guid? StickerId = null);
