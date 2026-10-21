import { CommentDto, FeedLikeDto, PostDto } from '../../core/models';
import { formatShortDate } from '../../shared/dates';

/** How many of a post's newest top-level comments its card shows. */
export const PREVIEW_COMMENTS = 2;

/** "now", "5m", "2h", "3d", then the date: how long ago, as a feed shows it. */
export function formatAgo(iso: string, now: Date = new Date()): string {
  const date = new Date(iso);
  const seconds = (now.getTime() - date.getTime()) / 1000;
  if (Number.isNaN(seconds)) return '';
  if (seconds < 60) return 'now';
  if (seconds < 3600) return `${Math.floor(seconds / 60)}m`;
  if (seconds < 86_400) return `${Math.floor(seconds / 3600)}h`;
  if (seconds < 7 * 86_400) return `${Math.floor(seconds / 86_400)}d`;
  return formatShortDate(date, now);
}

/** A post with a new like count; `likedByMe` only changes when it was the reader who liked. */
export function withLike(post: PostDto, like: FeedLikeDto, me: string | null): PostDto {
  if (like.postId !== post.id || like.commentId) return post;
  return {
    ...post,
    likeCount: like.likeCount,
    likedByMe: like.userId && like.userId === me ? like.liked : post.likedByMe,
  };
}

/** A comment with a new like count, the same way. */
export function withCommentLike(comment: CommentDto, like: FeedLikeDto, me: string | null): CommentDto {
  if (like.commentId !== comment.id) return comment;
  return {
    ...comment,
    likeCount: like.likeCount,
    likedByMe: like.userId && like.userId === me ? like.liked : comment.likedByMe,
  };
}

/** A post card after a new comment: one more, and a top-level one goes into the preview. */
export function withComment(post: PostDto, comment: CommentDto): PostDto {
  if (comment.postId !== post.id) return post;
  if (post.previewComments.some((c) => c.id === comment.id)) return post;
  const previewComments = comment.parentId
    ? post.previewComments
    : [...post.previewComments, comment].slice(-PREVIEW_COMMENTS);
  return { ...post, commentCount: post.commentCount + 1, previewComments };
}

/** A post card after a comment was deleted. */
export function withoutComment(post: PostDto, commentId: string): PostDto {
  const previewComments = post.previewComments.filter((c) => c.id !== commentId);
  return { ...post, commentCount: Math.max(0, post.commentCount - 1), previewComments };
}

/** Newest first, without duplicates: what arrived live may also come in a page. */
export function mergePosts(current: readonly PostDto[], incoming: readonly PostDto[]): PostDto[] {
  const byId = new Map(current.map((p) => [p.id, p]));
  for (const post of incoming) byId.set(post.id, post);
  return [...byId.values()].sort((a, b) => b.createdAt.localeCompare(a.createdAt));
}

/** A thread's comments: top-level ones in order, each with its replies (one level deep). */
export interface CommentNode {
  comment: CommentDto;
  replies: CommentDto[];
}

export function threadOf(comments: readonly CommentDto[]): CommentNode[] {
  const sorted = [...comments].sort((a, b) => a.createdAt.localeCompare(b.createdAt));
  const nodes = new Map<string, CommentNode>();
  for (const c of sorted) if (!c.parentId) nodes.set(c.id, { comment: c, replies: [] });
  for (const c of sorted) if (c.parentId) nodes.get(c.parentId)?.replies.push(c);
  return [...nodes.values()];
}

/** Whether the reader may delete it: their own, or anything for an admin. */
export function canDelete(author: { id: string; isBot: boolean }, me: string | null, canModerate: boolean): boolean {
  return canModerate || (!author.isBot && !!me && author.id === me);
}
