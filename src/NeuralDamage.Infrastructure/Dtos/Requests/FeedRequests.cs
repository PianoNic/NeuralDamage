namespace NeuralDamage.Infrastructure.Dtos.Requests;

/// <param name="Content">The text; may be empty when a sticker or a picture is sent.</param>
/// <param name="ImageId">A picture the caller generated for this post (<c>POST /api/images</c>), once it is ready.</param>
public record CreatePostRequest(string Content, Guid? StickerId = null, Guid? ImageId = null);

/// <param name="Prompt">What the picture shows.</param>
/// <param name="FaceBotId">A public bot with a profile picture to put in it, with its own face.</param>
public record GenerateImageRequest(string Prompt, Guid? FaceBotId = null);

/// <param name="Spec">The story: a background and layers; see <c>StorySpec</c>.</param>
public record CreateStoryRequest(NeuralDamage.Infrastructure.Services.Stories.StorySpec Spec);

/// <param name="Content">The reply, or a quick reaction such as an emoji.</param>
public record StoryReplyRequest(string Content);

/// <param name="ParentId">The comment this one answers, if any.</param>
public record CreateCommentRequest(string Content, Guid? ParentId = null, Guid? StickerId = null);
