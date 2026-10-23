import { Component, computed, inject, input, output, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideEllipsis, lucideHeart, lucideMessageCircle, lucideTrash2 } from '@ng-icons/lucide';
import { HlmBadge } from '@spartan-ng/helm/badge';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmCard } from '@spartan-ng/helm/card';
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

/** The credit for a picture from the web: its link and the site's name; null for anything but http(s). */
export function imageSource(url: string | null | undefined): { href: string; host: string } | null {
  if (!url) return null;
  try {
    const parsed = new URL(url);
    if (parsed.protocol !== 'https:' && parsed.protocol !== 'http:') return null;
    return { href: parsed.href, host: parsed.hostname.replace(/^www\./, '') };
  } catch {
    return null;
  }
}

/**
 * One post: who wrote it and when, the chat it came out of (only for those in that chat), its text
 * and sticker, Like and Comments, and on the feed the last two comments under it.
 */
@Component({
  selector: 'app-post-card',
  imports: [RouterLink, NgIcon, PkAuthImage, HlmBadge, HlmButton, HlmCard, HlmDialogImports, HlmDropdownMenuImports, MemberAvatar, StickerImage, FeedText],
  providers: [provideIcons({ lucideEllipsis, lucideHeart, lucideMessageCircle, lucideTrash2 })],
  host: { class: 'block' },
  template: `
    <article hlmCard class="gap-3 px-4" [attr.aria-label]="'Post by ' + post().author.name" data-testid="post">
      <header class="flex items-center gap-2.5">
        <app-member-avatar [name]="post().author.name" [avatarUrl]="post().author.avatarUrl" [modelId]="post().author.modelId" [px]="large() ? 44 : 40" />
        <div class="flex min-w-0 flex-1 flex-col gap-0.5">
          <div class="flex min-w-0 items-center gap-1.5">
            <span class="truncate font-semibold" [class.text-[15px]]="large()">{{ post().author.name }}</span>
            @if (post().author.isBot) {
              <span hlmBadge variant="outline">Bot</span>
            }
            <time class="text-muted-foreground shrink-0 text-[13px]" [attr.datetime]="post().createdAt" [title]="post().createdAt">· {{ ago() }}</time>
          </div>
          @if (sourceChat(); as chat) {
            <span class="text-muted-foreground truncate text-xs" data-testid="source-chat">after the chat in "{{ chat }}"</span>
          }
        </div>
        @if (deletable()) {
          <div class="shrink-0">
            <button hlmBtn variant="ghost" size="icon-sm" aria-label="Post options" [hlmDropdownMenuTrigger]="menu">
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
          </div>
        }
      </header>

      @if (post().content) {
        <app-feed-text class="leading-normal" [class.text-[15px]]="!large()" [class.text-base]="large()" [text]="post().content" [names]="names()" />
      }
      @if (post().image; as image) {
        <button
          type="button"
          class="bg-muted focus-visible:ring-ring/50 block w-full overflow-hidden rounded-xl outline-none focus-visible:ring-2"
          [style.aspect-ratio]="image.width && image.height ? image.width + ' / ' + image.height : '1 / 1'"
          [attr.aria-label]="'Open the picture: ' + image.prompt"
          data-testid="post-image"
          (click)="imageOpen.set(true)"
        >
          <pk-auth-image [url]="imageSrc()" [alt]="image.prompt" class="size-full" imgClass="size-full object-cover" />
        </button>
        @if (source(); as link) {
          <a
            class="text-muted-foreground hover:text-foreground -mt-1.5 w-fit truncate text-xs underline-offset-2 hover:underline"
            [href]="link.href"
            target="_blank"
            rel="noopener noreferrer nofollow"
            data-testid="post-image-source"
          >
            Source: {{ link.host }}
          </a>
        }
        <hlm-dialog [state]="imageOpen() ? 'open' : 'closed'" (closed)="imageOpen.set(false)">
          <hlm-dialog-content *hlmDialogPortal="let ctx" class="w-fit max-w-[calc(100vw-2rem)] p-2 sm:max-w-[min(90vw,72rem)]">
            <h2 hlmDialogTitle class="sr-only">Picture from {{ post().author.name }}</h2>
            <pk-auth-image [url]="imageSrc()" [alt]="image.prompt" class="min-h-40 min-w-40 rounded-lg" imgClass="max-h-[85vh] max-w-full object-contain" />
            @if (image.kind !== 'Composed') {
              <p hlmDialogDescription class="text-muted-foreground line-clamp-3 w-0 min-w-full px-1 pb-1 text-xs">{{ image.prompt }}</p>
            }
          </hlm-dialog-content>
        </hlm-dialog>
      }
      @if (post().sticker; as sticker) {
        <app-sticker-image [url]="sticker.url" [alt]="sticker.description || 'Sticker: ' + sticker.name" [px]="160" />
      }

      <div class="text-muted-foreground -ms-2 flex items-center gap-1 text-[13px]" [class.border-t]="large()" [class.pt-2]="large()">
        <button
          hlmBtn
          variant="ghost"
          size="sm"
          type="button"
          [class.text-destructive]="post().likedByMe"
          [attr.aria-pressed]="post().likedByMe"
          [attr.aria-label]="post().likedByMe ? 'Unlike' : 'Like'"
          (click)="like.emit()"
        >
          <ng-icon name="lucideHeart" [class.fill-current]="post().likedByMe" />
          <span class="tabular-nums">{{ post().likeCount }}</span>
        </button>
        @if (large()) {
          <span class="px-2">{{ post().commentCount }} {{ post().commentCount === 1 ? 'comment' : 'comments' }}</span>
        } @else {
          <a hlmBtn variant="ghost" size="sm" [routerLink]="['/feed', post().id]" [attr.aria-label]="'Comments: ' + post().commentCount">
            <ng-icon name="lucideMessageCircle" />
            <span class="tabular-nums">{{ post().commentCount }}</span>
          </a>
        }
      </div>

      @if (!large() && post().previewComments.length) {
        <div class="flex flex-col gap-2.5 border-t pt-3" data-testid="preview-comments">
          @for (comment of post().previewComments; track comment.id) {
            <div class="flex gap-2.5">
              <app-member-avatar [name]="comment.author.name" [avatarUrl]="comment.author.avatarUrl" [modelId]="comment.author.modelId" [px]="28" />
              <p class="min-w-0 text-sm leading-snug">
                <span class="font-semibold">{{ comment.author.name }}</span>
                @if (comment.content) {
                  <app-feed-text class="inline" [text]="' ' + comment.content" [names]="names()" />
                } @else {
                  <span class="text-muted-foreground"> sent a sticker</span>
                }
              </p>
            </div>
          }
          @if (post().commentCount > post().previewComments.length) {
            <a class="text-muted-foreground hover:text-foreground w-fit text-[13px]" [routerLink]="['/feed', post().id]">
              View all {{ post().commentCount }} comments
            </a>
          }
        </div>
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
  /** The thread's version: bigger, with the counts as text and no preview. */
  readonly large = input(false);
  /** For the relative time; the page ticks it. */
  readonly now = input<Date>(new Date());

  readonly like = output<void>();
  readonly remove = output<void>();

  protected readonly ago = computed(() => formatAgo(this.post().createdAt, this.now()));
  protected readonly imageOpen = signal(false);
  protected readonly imageSrc = computed(() => generatedImageSrc(this.post().image?.url ?? ''));
  /** The page a picture from the web came from: only an http(s) link is shown. */
  protected readonly source = computed(() => imageSource(this.post().image?.sourceUrl));
  /** Only the chat's members know its name; everyone else sees no source at all. */
  protected readonly sourceChat = computed(() => {
    const id = this.post().sourceChatId;
    return id ? (this.chats.chats().find((c) => c.id === id)?.name ?? null) : null;
  });
}
