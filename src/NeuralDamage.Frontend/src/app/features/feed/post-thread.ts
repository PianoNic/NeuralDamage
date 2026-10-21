import { NgTemplateOutlet } from '@angular/common';
import { Component, computed, DestroyRef, effect, inject, input, signal, untracked } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideChevronLeft, lucideHeart, lucideTrash2 } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmSidebarImports } from '@spartan-ng/helm/sidebar';
import { HlmSpinner } from '@spartan-ng/helm/spinner';
import { firstValueFrom } from 'rxjs';
import { FeedService } from '../../api/api/feed.service';
import { AuthService } from '../../core/auth/auth.service';
import { toastApiError } from '../../core/http-errors';
import { CommentDto, FeedLikeDto, Message, PostDto, StickerDto } from '../../core/models';
import { SignalRService } from '../../core/signalr/signalr.service';
import { MemberAvatar } from '../../shared/member-avatar';
import { BotDirectory } from '../bots/bot-directory';
import { Composer } from '../chat/composer';
import { Typer, TypingRow } from '../chat/typing-row';
import { StickerImage } from '../stickers/sticker-image';
import { canDelete, formatAgo, threadOf, withCommentLike, withLike } from './feed-model';
import { feedMembers, mentionNames } from './feed-members';
import { FeedStore } from './feed-store';
import { FeedText } from './feed-text';
import { PostCard } from './post-card';

/** A typing indicator lasts this long after the last ping, as in chats. */
const TYPING_TTL_MS = 6000;
const TYPING_SEND_INTERVAL_MS = 3000;

/**
 * `/feed/:postId`: a post and its whole conversation, replies one level deep under the comment they
 * answer, who is typing, and a comment box that takes @mentions to pull a bot in.
 */
@Component({
  selector: 'app-post-thread',
  imports: [
    NgTemplateOutlet,
    RouterLink,
    NgIcon,
    HlmButton,
    HlmSidebarImports,
    HlmSpinner,
    MemberAvatar,
    Composer,
    TypingRow,
    StickerImage,
    FeedText,
    PostCard,
  ],
  providers: [provideIcons({ lucideChevronLeft, lucideHeart, lucideTrash2 })],
  host: { class: 'flex h-full min-h-0 flex-col' },
  template: `
    <header class="flex h-12 shrink-0 items-center gap-2 border-b px-2 md:px-3">
      <button hlmSidebarTrigger></button>
      <a routerLink="/feed" class="text-muted-foreground hover:text-foreground flex items-center gap-1 text-sm">
        <ng-icon name="lucideChevronLeft" />
        Back to feed
      </a>
    </header>

    <div class="min-h-0 flex-1 overflow-y-auto">
      <div class="mx-auto flex w-full max-w-[680px] flex-col gap-4 px-4 py-6">
        @if (post(); as p) {
          <app-post-card [post]="p" [large]="true" [names]="names()" [now]="now()" [deletable]="deletable(p.author)" (like)="likePost()" (remove)="removePost()" />

          <section aria-label="Comments" class="flex flex-col gap-4">
            @for (node of thread(); track node.comment.id) {
              <div class="flex gap-2.5" data-testid="comment">
                <app-member-avatar [name]="node.comment.author.name" [avatarUrl]="node.comment.author.avatarUrl" [modelId]="node.comment.author.modelId" [px]="34" />
                <div class="flex min-w-0 flex-1 flex-col gap-1.5">
                  <ng-container *ngTemplateOutlet="bubble; context: { $implicit: node.comment }" />
                  @for (reply of node.replies; track reply.id) {
                    <div class="mt-1.5 flex gap-2.5" data-testid="reply">
                      <app-member-avatar [name]="reply.author.name" [avatarUrl]="reply.author.avatarUrl" [modelId]="reply.author.modelId" [px]="28" />
                      <div class="flex min-w-0 flex-1 flex-col gap-1.5">
                        <ng-container *ngTemplateOutlet="bubble; context: { $implicit: reply }" />
                      </div>
                    </div>
                  }
                </div>
              </div>
            } @empty {
              <p class="text-muted-foreground py-2 text-center text-sm">No comments yet. Say something, or @mention a bot.</p>
            }
            <app-typing-row class="ps-11" [typers]="typers()" />
          </section>

          <div class="bg-background sticky bottom-0 pb-2">
            <app-composer
              [feed]="true"
              [limit]="1000"
              [members]="members()"
              [mentionNames]="names()"
              [currentUserId]="me()"
              [replyingTo]="replyMessage()"
              placeholder="Comment, @mention a bot to pull it in"
              (send)="comment($event.content)"
              (sendSticker)="commentSticker($event)"
              (typing)="onTyping()"
              (cancelReply)="replyingTo.set(null)"
            />
          </div>
        } @else if (missing()) {
          <p class="text-muted-foreground py-10 text-center text-sm">This post is gone.</p>
        } @else {
          <div class="flex justify-center py-10"><hlm-spinner /></div>
        }
      </div>
    </div>

    <ng-template #bubble let-c>
      <div class="bg-muted w-fit max-w-full rounded-2xl px-3.5 py-2.5">
        <div class="mb-0.5 flex items-center gap-1.5 text-[13px]">
          <span class="font-semibold">{{ c.author.name }}</span>
          <time class="text-muted-foreground" [attr.datetime]="c.createdAt">· {{ ago(c.createdAt) }}</time>
        </div>
        @if (c.content) {
          <app-feed-text class="text-sm leading-snug" [text]="c.content" [names]="names()" />
        }
        @if (c.sticker; as sticker) {
          <app-sticker-image [url]="sticker.url" [alt]="sticker.description || 'Sticker: ' + sticker.name" [px]="120" />
        }
      </div>
      <div class="text-muted-foreground flex items-center gap-3 ps-1.5 text-xs">
        <button
          type="button"
          class="hover:text-foreground flex items-center gap-1"
          [class.text-destructive]="c.likedByMe"
          [attr.aria-pressed]="c.likedByMe"
          [attr.aria-label]="(c.likedByMe ? 'Unlike' : 'Like') + ' comment by ' + c.author.name"
          (click)="likeComment(c)"
        >
          <ng-icon name="lucideHeart" [class.fill-current]="c.likedByMe" />
          @if (c.likeCount) {
            <span class="tabular-nums">{{ c.likeCount }}</span>
          }
        </button>
        <button type="button" class="hover:text-foreground" (click)="replyingTo.set(c)">Reply</button>
        @if (deletable(c.author)) {
          <button type="button" class="hover:text-destructive" [attr.aria-label]="'Delete comment by ' + c.author.name" (click)="removeComment(c)">
            <ng-icon name="lucideTrash2" />
          </button>
        }
      </div>
    </ng-template>
  `,
})
export class PostThread {
  private readonly api = inject(FeedService);
  private readonly signalr = inject(SignalRService);
  private readonly auth = inject(AuthService);
  private readonly bots = inject(BotDirectory);
  private readonly store = inject(FeedStore);
  private readonly router = inject(Router);

  readonly postId = input.required<string>();

  protected readonly post = signal<PostDto | null>(null);
  protected readonly comments = signal<CommentDto[]>([]);
  protected readonly canModerate = signal(false);
  protected readonly missing = signal(false);
  protected readonly replyingTo = signal<CommentDto | null>(null);
  protected readonly now = signal(new Date());
  private readonly typing = signal<ReadonlyMap<string, Typer & { until: number }>>(new Map());

  protected readonly me = computed(() => this.auth.user()?.id ?? null);
  protected readonly thread = computed(() => threadOf(this.comments()));
  protected readonly members = computed(() => {
    const post = this.post();
    return feedMembers(this.bots.bots(), post ? [post] : [], this.comments());
  });
  protected readonly names = computed(() => mentionNames(this.members(), this.bots.bots()));
  protected readonly typers = computed(() => [...this.typing().values()].filter((t) => t.until > this.now().getTime()));
  /** The composer's reply chip reads a chat message; this is the comment in that shape. */
  protected readonly replyMessage = computed<Message | null>(() => {
    const c = this.replyingTo();
    if (!c) return null;
    return {
      id: c.id,
      chatId: '',
      senderUserId: c.author.isBot ? null : c.author.id,
      senderBotId: c.author.isBot ? c.author.id : null,
      senderName: c.author.name,
      senderAvatar: c.author.avatarUrl ?? null,
      senderModelId: c.author.modelId ?? null,
      senderType: c.author.isBot ? 'bot' : 'user',
      content: c.content,
      mentions: [],
      reactions: [],
      replyTo: null,
      attachments: [],
      sticker: c.sticker ?? null,
      createdAt: c.createdAt,
    };
  });

  private lastTypingSent = 0;
  private joined: string | null = null;

  constructor() {
    const tick = setInterval(() => this.now.set(new Date()), 1000);
    const onComment = (c: CommentDto) => {
      if (c.postId !== this.postId()) return;
      if (!this.comments().some((x) => x.id === c.id)) {
        this.comments.update((list) => [...list, c]);
        this.post.update((p) => (p ? { ...p, commentCount: p.commentCount + 1 } : p));
      }
      this.typing.update((map) => {
        const next = new Map(map);
        next.delete(c.author.id);
        return next;
      });
    };
    const onDeleted = (postId: string, commentId: string) => {
      if (postId !== this.postId()) return;
      this.comments.update((list) => list.filter((c) => c.id !== commentId));
      this.post.update((p) => (p ? { ...p, commentCount: Math.max(0, p.commentCount - 1) } : p));
    };
    const onLike = (like: FeedLikeDto) => {
      if (like.postId !== this.postId()) return;
      this.post.update((p) => (p ? withLike(p, like, this.me()) : p));
      this.comments.update((list) => list.map((c) => withCommentLike(c, like, this.me())));
    };
    const onTyping = (postId: string, authorId: string, name: string, isBot: boolean) => {
      if (postId !== this.postId() || authorId === this.me()) return;
      const bot = isBot ? this.bots.bots().find((b) => b.id === authorId) : undefined;
      this.typing.update((map) =>
        new Map(map).set(authorId, { id: authorId, name, avatarUrl: bot?.avatarUrl ?? null, modelId: bot?.modelId ?? null, until: Date.now() + TYPING_TTL_MS }),
      );
    };
    const onPostDeleted = (postId: string) => {
      if (postId === this.postId()) void this.router.navigate(['/feed']);
    };
    this.signalr.onChatEvent('FeedCommentNew', onComment);
    this.signalr.onChatEvent('FeedCommentDeleted', onDeleted);
    this.signalr.onChatEvent('FeedLikeUpdated', onLike);
    this.signalr.onChatEvent('FeedTyping', onTyping);
    this.signalr.onChatEvent('FeedPostDeleted', onPostDeleted);

    inject(DestroyRef).onDestroy(() => {
      clearInterval(tick);
      this.signalr.offChatEvent('FeedCommentNew', onComment);
      this.signalr.offChatEvent('FeedCommentDeleted', onDeleted);
      this.signalr.offChatEvent('FeedLikeUpdated', onLike);
      this.signalr.offChatEvent('FeedTyping', onTyping);
      this.signalr.offChatEvent('FeedPostDeleted', onPostDeleted);
      if (this.joined) void this.signalr.leavePost(this.joined);
    });

    effect(() => {
      const id = this.postId();
      untracked(() => void this.load(id));
    });
  }

  private async load(id: string): Promise<void> {
    void this.bots.load();
    void this.store.start();
    if (this.joined && this.joined !== id) void this.signalr.leavePost(this.joined);
    this.joined = id;
    void this.signalr.joinPost(id);
    this.post.set(null);
    this.missing.set(false);
    try {
      const thread = await firstValueFrom(this.api.apiFeedPostsPostIdGet(id));
      this.post.set(thread.post);
      this.comments.set(thread.comments);
      this.canModerate.set(thread.canModerate);
    } catch {
      this.missing.set(true);
    }
  }

  protected ago(at: string): string {
    return formatAgo(at, this.now());
  }

  protected deletable(author: { id: string; isBot: boolean }): boolean {
    return canDelete(author, this.me(), this.canModerate());
  }

  protected onTyping(): void {
    const now = Date.now();
    if (now - this.lastTypingSent < TYPING_SEND_INTERVAL_MS) return;
    this.lastTypingSent = now;
    this.signalr.startFeedTyping(this.postId());
  }

  protected async comment(content: string, stickerId: string | null = null): Promise<void> {
    const parent = this.replyingTo();
    try {
      const comment = await firstValueFrom(
        this.api.apiFeedPostsPostIdCommentsPost(this.postId(), { content, parentId: parent?.id ?? null, stickerId }),
      );
      this.replyingTo.set(null);
      if (!this.comments().some((c) => c.id === comment.id)) {
        this.comments.update((list) => [...list, comment]);
        this.post.update((p) => (p ? { ...p, commentCount: p.commentCount + 1 } : p));
      }
    } catch (error) {
      toastApiError(error, { network: 'Comment not sent. Check your connection and try again.' });
    }
  }

  protected commentSticker(sticker: StickerDto): Promise<void> {
    return this.comment('', sticker.id);
  }

  protected async likePost(): Promise<void> {
    try {
      const like = await firstValueFrom(this.api.apiFeedPostsPostIdLikePost(this.postId()));
      this.post.update((p) => (p ? withLike(p, like, this.me()) : p));
    } catch {
      toast.error('Could not like that post.');
    }
  }

  protected async likeComment(comment: CommentDto): Promise<void> {
    try {
      const like = await firstValueFrom(this.api.apiFeedPostsPostIdCommentsCommentIdLikePost(this.postId(), comment.id));
      this.comments.update((list) => list.map((c) => withCommentLike(c, like, this.me())));
    } catch {
      toast.error('Could not like that comment.');
    }
  }

  protected async removeComment(comment: CommentDto): Promise<void> {
    try {
      await firstValueFrom(this.api.apiFeedCommentsCommentIdDelete(comment.id));
      this.comments.update((list) => list.filter((c) => c.id !== comment.id && c.parentId !== comment.id));
    } catch (error) {
      toastApiError(error);
    }
  }

  protected async removePost(): Promise<void> {
    const post = this.post();
    if (!post) return;
    try {
      await this.store.remove(post);
      void this.router.navigate(['/feed']);
    } catch (error) {
      toastApiError(error);
    }
  }
}
