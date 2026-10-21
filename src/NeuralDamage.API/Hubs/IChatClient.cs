using NeuralDamage.Infrastructure.Dtos;

namespace NeuralDamage.API.Hubs;

public interface IChatClient
{
    Task ChatUpdated(ChatDto chat);
    Task ChatDeleted(Guid chatId);
    Task ChatCleared(Guid chatId);
    Task MemberAdded(ChatMemberDto member);
    Task MemberRemoved(Guid chatId, Guid memberId);
    Task MessageNew(MessageDto message);
    Task ReactionUpdated(Guid messageId, List<ReactionGroupDto> reactions);
    Task BotTyping(Guid chatId, Guid botId, string botName);
    Task UserTyping(Guid chatId, Guid userId, string displayName);
    Task BotResponseCancelled(Guid chatId);
    Task SystemMessage(SystemMessageDto message);
    Task AttachmentDescribed(Guid chatId, Guid messageId, Guid attachmentId, string description);

    // The feed: posts, comments and likes go to everyone, typing to the post's thread, replies to one user.
    Task FeedPostNew(PostDto post);
    Task FeedPostDeleted(Guid postId);
    Task FeedCommentNew(CommentDto comment);
    Task FeedCommentDeleted(Guid postId, Guid commentId);
    Task FeedLikeUpdated(FeedLikeDto like);
    Task FeedTyping(Guid postId, Guid authorId, string name, bool isBot);
    Task FeedReply(FeedReplyNoticeDto notice);
}
