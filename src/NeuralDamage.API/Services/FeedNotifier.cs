using Microsoft.AspNetCore.SignalR;
using NeuralDamage.API.Hubs;
using NeuralDamage.Infrastructure.Dtos;
using NeuralDamage.Infrastructure.Services.Feed;

namespace NeuralDamage.API.Services;

/// <summary>
/// The feed's live updates over the chat hub. Every connection is signed in
/// and everyone sees the same feed, so posts, comments and likes go to all;
/// what differs per reader (whether they liked it, the source chat's name)
/// the client works out itself.
/// </summary>
public class FeedNotifier(IHubContext<ChatHub, IChatClient> hub) : IFeedNotifier
{
    public Task PostNew(PostDto post) => hub.Clients.All.FeedPostNew(post);
    public Task PostDeleted(Guid postId) => hub.Clients.All.FeedPostDeleted(postId);
    public Task CommentNew(CommentDto comment) => hub.Clients.All.FeedCommentNew(comment);
    public Task CommentDeleted(Guid postId, Guid commentId) => hub.Clients.All.FeedCommentDeleted(postId, commentId);
    public Task LikeUpdated(FeedLikeDto like) => hub.Clients.All.FeedLikeUpdated(like);
    public Task Typing(Guid postId, Guid authorId, string name, bool isBot) => hub.Clients.Group(ChatHub.PostGroup(postId)).FeedTyping(postId, authorId, name, isBot);
    public Task Reply(Guid userId, FeedReplyNoticeDto notice) => hub.Clients.Group(ChatHub.UserGroup(userId)).FeedReply(notice);
    public Task StoryNew(StoryDto story) => hub.Clients.All.StoryNew(story);
    public Task StoryDeleted(Guid storyId) => hub.Clients.All.StoryDeleted(storyId);
}
