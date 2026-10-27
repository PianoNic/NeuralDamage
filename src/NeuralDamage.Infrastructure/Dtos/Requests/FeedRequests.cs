namespace NeuralDamage.Infrastructure.Dtos.Requests;

/// <param name="Content">The text; may be empty when a sticker or a picture is sent.</param>
/// <param name="ImageId">A picture the caller generated for this post (<c>POST /api/images</c>), once it is ready.</param>
/// <param name="SongId">A song for the post to play (<c>POST /api/music/songs</c>).</param>
/// <param name="MusicStart">Where its clip starts in the whole song, in seconds; left out, or without the whole song stored, it plays Deezer's preview.</param>
public record CreatePostRequest(string Content, Guid? StickerId = null, Guid? ImageId = null, Guid? SongId = null, int? MusicStart = null);

/// <param name="Prompt">What the picture shows.</param>
/// <param name="FaceBotId">A public bot with a profile picture to put in it, with its own face.</param>
/// <param name="ForStory">A story's background, 9:16; otherwise a square picture for a post.</param>
/// <param name="Format">The background of a picture being composed: "story", "post-4x5" or "post-1x1". Overrides nothing when ForStory is set.</param>
public record GenerateImageRequest(string Prompt, Guid? FaceBotId = null, bool ForStory = false, string? Format = null);

/// <param name="Query">The search the result came from, as sent to <c>GET /api/images/search</c>.</param>
/// <param name="Index">The result's place in that search.</param>
public record PickWebImageRequest(string Query, int Index);

/// <param name="Spec">The picture's layers, in a post's format ("post-4x5" or "post-1x1"); see <c>StorySpec</c>.</param>
public record ComposeImageRequest(NeuralDamage.Infrastructure.Services.Stories.StorySpec Spec);

/// <param name="Spec">The story: a background and layers; see <c>StorySpec</c>.</param>
/// <param name="SongId">A song for the story to play (<c>POST /api/music/songs</c>).</param>
/// <param name="MusicStart">Where its clip starts in the whole song, in seconds; left out, it plays Deezer's preview.</param>
public record CreateStoryRequest(NeuralDamage.Infrastructure.Services.Stories.StorySpec Spec, Guid? SongId = null, int? MusicStart = null);

/// <param name="Content">The reply, or a quick reaction such as an emoji.</param>
public record StoryReplyRequest(string Content);

/// <param name="ParentId">The comment this one answers, if any.</param>
public record CreateCommentRequest(string Content, Guid? ParentId = null, Guid? StickerId = null);

/// <param name="DeezerId">The song's id on Deezer, from <c>GET /api/music/search</c>.</param>
public record AddSongRequest(long DeezerId);
