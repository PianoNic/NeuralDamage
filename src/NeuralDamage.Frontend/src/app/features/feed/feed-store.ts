import { inject, Service, signal } from '@angular/core';
import { Router } from '@angular/router';
import { toast } from '@spartan-ng/brain/sonner';
import { firstValueFrom } from 'rxjs';
import { FeedService } from '../../api/api/feed.service';
import { AuthService } from '../../core/auth/auth.service';
import { CommentDto, FeedActivityDto, FeedLikeDto, PostDto } from '../../core/models';
import { FeedReplyNoticeDto } from '../../core/signalr/hub-events';
import { SignalRService } from '../../core/signalr/signalr.service';
import { mergePosts, withComment, withLike, withoutComment } from './feed-model';

const PAGE = 20;

/**
 * The feed as the app keeps it: the posts loaded so far, kept live from the hub, the "Happening
 * now" panel, and the unread count the sidebar shows while the feed is not open.
 */
@Service()
export class FeedStore {
  private readonly api = inject(FeedService);
  private readonly signalr = inject(SignalRService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  readonly posts = signal<PostDto[]>([]);
  /** Posts deleted while the app is open, so a profile's own copy drops them too. */
  readonly removed = signal<ReadonlySet<string>>(new Set());
  readonly loading = signal(false);
  readonly hasMore = signal(true);
  readonly canModerate = signal(false);
  readonly activity = signal<FeedActivityDto | null>(null);
  /** New posts and replies to the user since they last had the feed open. */
  readonly unread = signal(0);
  /** The feed page is on screen, so nothing arriving is unread. */
  readonly open = signal(false);

  private started = false;
  private loadedOnce = false;

  get me(): string | null {
    return this.auth.user()?.id ?? null;
  }

  /** Subscribes to the hub and reads the unread count; called once by the sidebar. */
  async start(): Promise<void> {
    if (this.started) return;
    this.started = true;
    this.signalr.onChatEvent('FeedPostNew', (post) => this.onPost(post));
    this.signalr.onChatEvent('FeedPostDeleted', (postId) => this.drop(postId));
    this.signalr.onChatEvent('FeedCommentNew', (comment) => this.onComment(comment));
    this.signalr.onChatEvent('FeedCommentDeleted', (postId, commentId) =>
      this.posts.update((list) => list.map((p) => (p.id === postId ? withoutComment(p, commentId) : p))),
    );
    this.signalr.onChatEvent('FeedLikeUpdated', (like: FeedLikeDto) =>
      this.posts.update((list) => list.map((p) => withLike(p, like, this.me))),
    );
    this.signalr.onChatEvent('FeedReply', (notice) => this.onReply(notice));
    this.signalr.reconnected$.subscribe(() => void this.catchUp());
    void this.signalr.start();
    try {
      this.unread.set(await firstValueFrom(this.api.apiFeedUnreadGet()));
    } catch {
      // The badge stays empty.
    }
  }

  /** Posts, comments and likes sent while disconnected are lost; read the feed and the badge again. */
  private async catchUp(): Promise<void> {
    if (this.loadedOnce) void this.loadFirst();
    if (this.open()) return;
    try {
      this.unread.set(await firstValueFrom(this.api.apiFeedUnreadGet()));
    } catch {
      // The badge keeps what it had.
    }
  }

  /** The first page, read again whenever the feed is opened. */
  async loadFirst(): Promise<void> {
    this.loading.set(true);
    try {
      const page = await firstValueFrom(this.api.apiFeedPostsGet(undefined, PAGE));
      this.posts.set(this.loadedOnce ? mergePosts(this.posts(), page.posts).slice(0, Math.max(PAGE, page.posts.length)) : page.posts);
      this.loadedOnce = true;
      this.canModerate.set(page.canModerate);
      this.hasMore.set(page.posts.length === PAGE);
    } catch {
      toast.error('Could not load the feed.');
    } finally {
      this.loading.set(false);
    }
    void this.loadActivity();
  }

  async loadMore(): Promise<void> {
    const last = this.posts().at(-1);
    if (!last || this.loading() || !this.hasMore()) return;
    this.loading.set(true);
    try {
      const page = await firstValueFrom(this.api.apiFeedPostsGet(last.createdAt, PAGE));
      this.posts.update((list) => mergePosts(list, page.posts));
      this.hasMore.set(page.posts.length === PAGE);
    } catch {
      toast.error('Could not load older posts.');
    } finally {
      this.loading.set(false);
    }
  }

  async loadActivity(): Promise<void> {
    try {
      this.activity.set(await firstValueFrom(this.api.apiFeedActivityGet()));
    } catch {
      // The panel keeps what it had.
    }
  }

  /** The feed is on screen: everything up to now is seen. */
  async markSeen(): Promise<void> {
    this.unread.set(0);
    await firstValueFrom(this.api.apiFeedSeenPost()).catch(() => undefined);
  }

  async post(content: string, stickerId: string | null = null, imageId: string | null = null): Promise<void> {
    const post = await firstValueFrom(this.api.apiFeedPostsPost({ content, stickerId, imageId }));
    this.posts.update((list) => mergePosts(list, [post]));
  }

  async toggleLike(post: PostDto): Promise<void> {
    const like = await firstValueFrom(this.api.apiFeedPostsPostIdLikePost(post.id));
    this.posts.update((list) => list.map((p) => withLike(p, like, this.me)));
  }

  async remove(post: PostDto): Promise<void> {
    await firstValueFrom(this.api.apiFeedPostsPostIdDelete(post.id));
    this.drop(post.id);
  }

  private drop(postId: string): void {
    this.posts.update((list) => list.filter((p) => p.id !== postId));
    this.removed.update((ids) => new Set(ids).add(postId));
  }

  private onPost(post: PostDto): void {
    // A page not yet loaded reads it then.
    if (this.loadedOnce) this.posts.update((list) => mergePosts(list, [post]));
    if (!this.open() && post.author.id !== this.me) this.unread.update((n) => n + 1);
    if (this.open()) void this.loadActivity();
  }

  private onComment(comment: CommentDto): void {
    this.posts.update((list) => list.map((p) => withComment(p, comment)));
    if (this.open()) void this.loadActivity();
  }

  private onReply(notice: FeedReplyNoticeDto): void {
    if (!this.open()) this.unread.update((n) => n + 1);
    // On the thread itself the comment shows up anyway.
    if (this.router.url.startsWith(`/feed/${notice.postId}`)) return;
    toast(`${notice.authorName} ${notice.onYourPost ? 'commented on your post' : 'replied to you'}`, {
      description: notice.content,
      action: { label: 'View', onClick: () => void this.router.navigate(['/feed', notice.postId]) },
    });
  }
}
