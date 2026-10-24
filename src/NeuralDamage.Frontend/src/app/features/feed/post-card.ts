import { Component, computed, inject, input, output, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideEllipsis, lucideHeart, lucideMessageCircle, lucideTrash2 } from '@ng-icons/lucide';
import { HlmBadge } from '@spartan-ng/helm/badge';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import { HlmDropdownMenuImports } from '@spartan-ng/helm/dropdown-menu';
import { PkAuthImage } from '@prompt-kit/auth-image';
import { PostDto } from '../../core/models';
import { MemberAvatar } from '../../shared/member-avatar';
import { ChatList } from '../chat/chat-list';
import { StickerImage } from '../stickers/sticker-image';
import { formatAgo } from './feed-model';
import { FeedText } from './feed-text';
import { generatedImageSrc } from './generate-image';

/**
 * One post, after Instagram's: who wrote it and when (and the chat it came out of, only for those in
 * that chat), the picture or the text, Like and Comments, and the caption under a picture.
 */
@Component({
  selector: 'app-post-card',
  imports: [RouterLink, NgIcon, PkAuthImage, HlmBadge, HlmButton, HlmDialogImports, HlmDropdownMenuImports, MemberAvatar, StickerImage, FeedText],
  providers: [provideIcons({ lucideEllipsis, lucideHeart, lucideMessageCircle, lucideTrash2 })],
  host: { class: 'block' },
  template: `
    <article class="flex flex-col gap-2.5" [attr.aria-label]="'Post by ' + post().author.name" data-testid="post">
      <header class="flex items-center gap-2.5 px-3 md:px-0">
        <app-member-avatar [name]="post().author.name" [avatarUrl]="post().author.avatarUrl" [modelId]="post().author.modelId" [px]="large() ? 40 : 36" />
        <div class="flex min-w-0 flex-1 flex-col">
          <div class="flex min-w-0 items-center gap-1.5 text-sm">
            @if (post().author.isBot) {
              <a [routerLink]="['/bots', post().author.id]" class="truncate font-semibold underline-offset-2 hover:underline">{{ post().author.name }}</a>
            } @else {
              <span class="truncate font-semibold">{{ post().author.name }}</span>
            }
            @if (post().author.isBot) {
              <span hlmBadge class="bg-bot text-bot-foreground h-4 rounded px-1 text-[10px] font-extrabold tracking-wide">BOT</span>
            }
            <time class="text-muted-foreground shrink-0" [attr.datetime]="post().createdAt" [title]="post().createdAt">· {{ ago() }}</time>
          </div>
          @if (sourceChat(); as chat) {
            <span class="text-muted-foreground truncate text-xs" data-testid="source-chat">after the chat in "{{ chat }}"</span>
          }
        </div>
        @if (deletable()) {
          <button hlmBtn variant="ghost" size="icon-sm" aria-label="Post options" [hlmDropdownMenuTrigger]="menu" align="end">
            <ng-icon name="lucideEllipsis" />
          </button>
          <ng-template #menu>
            <hlm-dropdown-menu class="w-40">
              <button hlmDropdownMenuItem variant="destructive" (triggered)="remove.emit()">
                <ng-icon name="lucideTrash2" />
                Delete post
              </button>
            </hlm-dropdown-menu>
          </ng-template>
        }
      </header>

      @if (showImage() && post().image; as image) {
        <button
          type="button"
          class="bg-muted focus-visible:ring-ring/50 mx-2 block overflow-hidden rounded-2xl outline-none focus-visible:ring-2 md:mx-0"
          [style.aspect-ratio]="image.width && image.height ? image.width + ' / ' + image.height : '1 / 1'"
          [attr.aria-label]="'Open the picture: ' + image.prompt"
          data-testid="post-image"
          (click)="imageOpen.set(true)"
        >
          <pk-auth-image [url]="imageSrc()" [alt]="image.prompt" class="size-full" imgClass="size-full object-cover" />
        </button>
        <hlm-dialog [state]="imageOpen() ? 'open' : 'closed'" (closed)="imageOpen.set(false)">
          <hlm-dialog-content *hlmDialogPortal="let ctx" class="w-fit max-w-[calc(100vw-2rem)] p-2 sm:max-w-[min(90vw,72rem)]">
            <h2 hlmDialogTitle class="sr-only">Picture from {{ post().author.name }}</h2>
            <pk-auth-image [url]="imageSrc()" [alt]="image.prompt" class="min-h-40 min-w-40 rounded-lg" imgClass="max-h-[85vh] max-w-full object-contain" />
            @if (image.kind !== 'Composed') {
              <p hlmDialogDescription class="text-muted-foreground line-clamp-3 w-0 min-w-full px-1 pb-1 text-xs">{{ image.prompt }}</p>
            }
          </hlm-dialog-content>
        </hlm-dialog>
      } @else if (post().content && (!post().image || showImage())) {
        <app-feed-text class="px-3 text-[15px] leading-normal md:px-0" [class.text-base]="large()" [text]="post().content" [names]="names()" />
      }
      @if (post().sticker; as sticker) {
        <app-sticker-image class="px-3 md:px-0" [url]="sticker.url" [alt]="sticker.description || 'Sticker: ' + sticker.name" [px]="160" />
      }

      <div class="flex items-center gap-1 px-1 text-sm font-semibold md:-ms-2 md:px-0">
        <button
          hlmBtn
          variant="ghost"
          size="sm"
          type="button"
          [attr.aria-pressed]="post().likedByMe"
          [attr.aria-label]="post().likedByMe ? 'Unlike' : 'Like'"
          (click)="like.emit()"
        >
          <ng-icon name="lucideHeart" size="22" [class.text-highlight]="post().likedByMe" [class.fill-current]="post().likedByMe" />
          <span class="tabular-nums">{{ post().likeCount }}</span>
        </button>
        @if (large()) {
          <span class="flex items-center gap-1.5 px-2"><ng-icon name="lucideMessageCircle" size="22" class="-scale-x-100" />{{ post().commentCount }}</span>
        } @else {
          <a hlmBtn variant="ghost" size="sm" [routerLink]="['/feed', post().id]" [attr.aria-label]="'Comments: ' + post().commentCount">
            <ng-icon name="lucideMessageCircle" size="22" class="-scale-x-100" />
            <span class="tabular-nums">{{ post().commentCount }}</span>
          </a>
        }
      </div>

      @if (post().image && post().content) {
        <p class="px-3 text-sm leading-normal md:px-0">
          <span class="font-semibold">{{ post().author.name }}</span>
          <app-feed-text class="inline" [text]="' ' + post().content" [names]="names()" />
        </p>
      }
    </article>
  `,
})
export class PostCard {
  private readonly chats = inject(ChatList);

  readonly post = input.required<PostDto>();
  /** Names that are @mentions: the bots' and people's. */
  readonly names = input<readonly string[]>([]);
  readonly deletable = input(false);
  /** The thread's version: bigger, with the comment count as text instead of a link. */
  readonly large = input(false);
  /** Off where the page shows the picture itself, beside the post (the desktop thread). */
  readonly showImage = input(true);
  /** For the relative time; the page ticks it. */
  readonly now = input<Date>(new Date());

  readonly like = output<void>();
  readonly remove = output<void>();

  protected readonly ago = computed(() => formatAgo(this.post().createdAt, this.now()));
  protected readonly imageOpen = signal(false);
  protected readonly imageSrc = computed(() => generatedImageSrc(this.post().image?.url ?? ''));
  /** Only the chat's members know its name; everyone else sees no source at all. */
  protected readonly sourceChat = computed(() => {
    const id = this.post().sourceChatId;
    return id ? (this.chats.chats().find((c) => c.id === id)?.name ?? null) : null;
  });
}
