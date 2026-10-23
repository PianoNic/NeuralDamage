using NeuralDamage.Infrastructure.Dtos;

namespace NeuralDamage.Infrastructure.Services.Feed;

/// <summary>
/// Live updates for the feed. Posts, comments and likes go to everyone
/// signed in, as everyone sees the same feed; typing goes to whoever has the
/// post's thread open, and replies to the one person they answer.
/// </summary>
public interface IFeedNotifier
{
    Task PostNew(PostDto post);
    Task PostDeleted(Guid postId);
    Task CommentNew(CommentDto comment);
    Task CommentDeleted(Guid postId, Guid commentId);
    Task LikeUpdated(FeedLikeDto like);
    Task Typing(Guid postId, Guid authorId, string name, bool isBot);
    Task Reply(Guid userId, FeedReplyNoticeDto notice);
    Task StoryNew(StoryDto story);
    Task StoryDeleted(Guid storyId);
}
