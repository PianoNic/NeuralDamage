import { NgTemplateOutlet } from '@angular/common';
import { Component, computed, DestroyRef, effect, inject, input, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideChevronLeft, lucideHeart, lucideTrash2 } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { PkAuthImage } from '@prompt-kit/auth-image';
import { HlmBadge } from '@spartan-ng/helm/badge';
import { HlmButton } from '@spartan-ng/helm/button';
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
import { generatedImageSrc } from './generate-image';
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
    PkAuthImage,
    HlmBadge,
    HlmButton,
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
    <header class="flex h-14 shrink-0 items-center gap-1 border-b px-2">
      <a hlmBtn variant="ghost" size="icon" routerLink="/" aria-label="Back to feed">
        <ng-icon name="lucideChevronLeft" />
      </a>
      <h1 class="font-display text-xl font-extrabold tracking-tight">Post</h1>
    </header>

    @if (post(); as p) {
      <div class="flex min-h-0 flex-1">
        @if (p.image; as image) {
          <div class="hidden min-w-0 flex-1 items-center justify-center bg-black lg:flex">
            <pk-auth-image [url]="imageSrc()" [alt]="image.prompt" class="size-full" imgClass="size-full object-contain" />
          </div>
        }
        <div class="flex min-h-0 w-full flex-col" [class.lg:w-[460px]]="!!p.image" [class.lg:flex-none]="!!p.image" [class.lg:border-s]="!!p.image">
          <div class="min-h-0 flex-1 overflow-y-auto">
            <div class="mx-auto flex w-full max-w-[680px] flex-col gap-5 py-4 md:px-4">
              <app-post-card class="lg:hidden" [post]="p" [large]="true" [names]="names()" [now]="now()" [deletable]="deletable(p.author)" (like)="likePost()" (remove)="removePost()" />
              <app-post-card class="hidden lg:block" [post]="p" [large]="true" [showImage]="!p.image" [names]="names()" [now]="now()" [deletable]="deletable(p.author)" (like)="likePost()" (remove)="removePost()" />

              <section aria-label="Comments" class="flex flex-col gap-4 border-t px-3 pt-4 md:px-0">
                @for (node of thread(); track node.comment.id) {
                  <div class="flex flex-col gap-3" data-testid="comment">
                    <ng-container *ngTemplateOutlet="row; context: { $implicit: node.comment, px: 32 }" />
                    @for (reply of node.replies; track reply.id) {
                      <div class="ps-11" data-testid="reply">
                        <ng-container *ngTemplateOutlet="row; context: { $implicit: reply, px: 26 }" />
                      </div>
                    }
                  </div>
                } @empty {
                  <p class="text-muted-foreground py-6 text-center text-sm">No comments yet. Say something, or @mention a bot.</p>
                }
                <app-typing-row class="ps-11" [typers]="typers()" />
              </section>
            </div>
          </div>

          <div class="bg-background border-t px-3 py-2 md:px-4">
            <div class="mx-auto max-w-[680px]">
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
          </div>
        </div>
      </div>
    } @else if (missing()) {
      <p class="text-muted-foreground py-10 text-center text-sm">This post is gone.</p>
    } @else {
      <div class="flex justify-center py-10"><hlm-spinner /></div>
    }

    <ng-template #row let-c let-px="px">
      <div class="flex gap-3">
        <app-member-avatar [name]="c.author.name" [avatarUrl]="c.author.avatarUrl" [modelId]="c.author.modelId" [px]="px" />
        <div class="min-w-0 flex-1">
          <p class="text-sm leading-snug">
            <span class="font-semibold">{{ c.author.name }}</span>
            @if (c.author.isBot) {
              <span hlmBadge class="bg-bot text-bot-foreground ms-1 h-4 rounded px-1 align-middle text-[10px] font-extrabold tracking-wide">BOT</span>
            }
            @if (c.content) {
              <app-feed-text class="inline" [text]="' ' + c.content" [names]="names()" />
            }
          </p>
          @if (c.sticker; as sticker) {
            <app-sticker-image class="mt-1" [url]="sticker.url" [alt]="sticker.description || 'Sticker: ' + sticker.name" [px]="120" />
          }
          <div class="text-muted-foreground mt-1 flex items-center gap-3 text-xs font-semibold">
            <time [attr.datetime]="c.createdAt">{{ ago(c.createdAt) }}</time>
            @if (c.likeCount) {
              <span>{{ c.likeCount }} {{ c.likeCount === 1 ? 'like' : 'likes' }}</span>
            }
            <button type="button" class="hover:text-foreground" (click)="replyingTo.set(c)">Reply</button>
            @if (c.likedByAuthor && post(); as p) {
              <!-- Instagram's "liked by the creator": the post's author, with a small heart. -->
              <span class="relative flex" [attr.title]="'Liked by ' + p.author.name" role="img" [attr.aria-label]="'Liked by ' + p.author.name">
                <app-member-avatar [name]="p.author.name" [avatarUrl]="p.author.avatarUrl" [modelId]="p.author.modelId" [px]="18" />
                <span class="bg-background absolute -end-1 -bottom-1 flex rounded-full p-px">
                  <ng-icon name="lucideHeart" size="9" class="text-highlight [&_svg]:fill-current" />
                </span>
              </span>
            }
            @if (deletable(c.author)) {
              <button type="button" class="hover:text-destructive" [attr.aria-label]="'Delete comment by ' + c.author.name" (click)="removeComment(c)">
                <ng-icon name="lucideTrash2" />
              </button>
            }
          </div>
        </div>
        <button
          type="button"
          class="text-muted-foreground hover:text-foreground flex h-fit items-center pt-1"
          [attr.aria-pressed]="c.likedByMe"
          [attr.aria-label]="(c.likedByMe ? 'Unlike' : 'Like') + ' comment by ' + c.author.name"
          (click)="likeComment(c)"
        >
          <ng-icon name="lucideHeart" size="14" [class]="c.likedByMe ? 'text-highlight [&_svg]:fill-current' : ''" />
        </button>
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
  protected readonly imageSrc = computed(() => generatedImageSrc(this.post()?.image?.url ?? ''));
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
      story: null,
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
      if (postId === this.postId()) void this.router.navigate(['/']);
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

    this.signalr.reconnected$.pipe(takeUntilDestroyed()).subscribe(() => void this.refresh());
  }

  /** After a reconnect: comments and likes sent meanwhile are lost, and anyone typing has stopped. */
  private async refresh(): Promise<void> {
    const id = this.postId();
    this.typing.set(new Map());
    try {
      const thread = await firstValueFrom(this.api.apiFeedPostsPostIdGet(id));
      if (id !== this.postId()) return;
      this.post.set(thread.post);
      this.comments.set(thread.comments);
    } catch {
      // The thread keeps what it shows; a deleted post arrives as FeedPostDeleted or a 404 on reload.
    }
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
      void this.router.navigate(['/']);
    } catch (error) {
      toastApiError(error);
    }
  }
}
