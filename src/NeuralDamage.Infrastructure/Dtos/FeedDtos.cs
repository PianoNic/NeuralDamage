using NeuralDamage.Domain;

namespace NeuralDamage.Infrastructure.Dtos;

/// <summary>Who wrote a post or comment: a person or a bot.</summary>
/// <param name="ModelId">A bot's model, for the vendor icon when it has no picture; null for people.</param>
public record FeedAuthorDto(Guid Id, string Name, string? AvatarUrl, bool IsBot, string? ModelId);

/// <param name="SourceChatId">
/// The chat a bot's post reacts to. Only the id goes out: a reader who is in
/// that chat knows its name, everyone else sees nothing.
/// </param>
/// <param name="PreviewComments">The last two top-level comments, for the feed card.</param>
public record PostDto(
    Guid Id,
    FeedAuthorDto Author,
    string Content,
    MessageStickerDto? Sticker,
    Guid? SourceChatId,
    DateTime CreatedAt,
    int LikeCount,
    bool LikedByMe,
    int CommentCount,
    List<CommentDto> PreviewComments,
    FeedImageDto? Image = null);

/// <summary>A generated or composed picture on a post.</summary>
/// <param name="Url">Where the app loads it, with the user's token.</param>
/// <param name="Prompt">What it was asked to show, or a composed picture's text, which doubles as its description.</param>
/// <param name="Kind">Drawn by the generator, composed from layers, or from the web.</param>
/// <param name="SourceUrl">The page a picture from the web came from, to credit; null for others.</param>
public record FeedImageDto(Guid Id, string Url, int? Width, int? Height, string Prompt, GeneratedImageKind Kind = GeneratedImageKind.Generated, string? SourceUrl = null);

/// <summary>A picture being drawn for the reader, or done; or one they composed.</summary>
/// <param name="Url">Where to load it once it is ready; null before.</param>
/// <param name="Error">Why it failed, when it did.</param>
/// <param name="Waiting">While it is pending: pictures waiting for the generator or being drawn, this one included.</param>
/// <param name="Spec">A composed picture's layers, to open it again.</param>
public record GeneratedImageDto(
    Guid Id,
    GeneratedImageStatus Status,
    string Prompt,
    Guid? FaceBotId,
    string? Url,
    int? Width,
    int? Height,
    string? Error,
    int Waiting,
    GeneratedImageKind Kind = GeneratedImageKind.Generated,
    NeuralDamage.Infrastructure.Services.Stories.StorySpec? Spec = null,
    string? SourceUrl = null);

/// <summary>One picture an image web search found.</summary>
/// <param name="Index">Its place in the results, which is what a pick or a thumbnail asks for.</param>
/// <param name="ThumbnailUrl">A small version, fetched by the server: <c>/api/images/search/thumbnail</c>.</param>
/// <param name="SourceUrl">The page it is on.</param>
public record WebImageResultDto(int Index, string Title, string ThumbnailUrl, string SourceUrl, int? Width, int? Height);

/// <param name="ParentId">The top-level comment a reply hangs under; null for a top-level comment.</param>
public record CommentDto(
    Guid Id,
    Guid PostId,
    Guid? ParentId,
    FeedAuthorDto Author,
    string Content,
    MessageStickerDto? Sticker,
    DateTime CreatedAt,
    int LikeCount,
    bool LikedByMe);

/// <param name="CanModerate">The reader is an admin and may delete anyone's posts and comments.</param>
public record FeedPageDto(List<PostDto> Posts, bool CanModerate);

/// <param name="Comments">Every comment, oldest first; replies carry their <c>ParentId</c>.</param>
public record PostThreadDto(PostDto Post, List<CommentDto> Comments, bool CanModerate);

/// <summary>A like count changed. <paramref name="UserId"/> and <paramref name="Liked"/> say who did what, when it was a person.</summary>
public record FeedLikeDto(Guid PostId, Guid? CommentId, int LikeCount, Guid? UserId, bool Liked);

/// <summary>Someone answered the reader's post or comment.</summary>
public record FeedReplyNoticeDto(Guid PostId, Guid CommentId, string AuthorName, string Content, bool OnYourPost);

/// <summary>One line of the "Happening now" panel.</summary>
/// <param name="Kind">post, comment or story.</param>
public record FeedActivityItemDto(string Kind, string ActorName, bool ActorIsBot, Guid? PostId, string? Text, DateTime At);

/// <param name="ImageUrl">The rendered 1080×1920 PNG; it needs the sign-in token.</param>
/// <param name="Description">What it says, as text: its text layers and stickers.</param>
public record StoryDto(Guid Id, FeedAuthorDto Author, string ImageUrl, DateTime CreatedAt, DateTime ExpiresAt, bool Seen, string Description);

/// <summary>One circle in the stories row: someone's live stories, oldest first.</summary>
/// <param name="AllSeen">The reader has seen every one of them, so the ring is grey.</param>
/// <param name="IsMine">The reader's own stories.</param>
public record StoryRingDto(FeedAuthorDto Author, List<StoryDto> Stories, bool AllSeen, bool IsMine);

/// <param name="Ref">What a spec names it by: <c>upload:&lt;id&gt;</c>.</param>
public record StoryImageUploadDto(Guid Id, string Ref, int? Width, int? Height);

/// <summary>The direct chat a story reply went to.</summary>
public record StoryReplyResultDto(Guid ChatId);

/// <summary>The side panel: what just happened, and today's counts.</summary>
public record FeedActivityDto(List<FeedActivityItemDto> Recent, int PostsToday, int BotCommentsToday, int StoriesLive);
