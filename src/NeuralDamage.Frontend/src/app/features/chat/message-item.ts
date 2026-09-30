import { Component, computed, input, output } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideReply, lucideSmilePlus } from '@ng-icons/lucide';
import { PkMarkdown } from '@prompt-kit/markdown';
import { HlmAvatarImports } from '@spartan-ng/helm/avatar';
import { HlmBadge } from '@spartan-ng/helm/badge';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmPopoverImports } from '@spartan-ng/helm/popover';
import { HlmTooltipImports } from '@spartan-ng/helm/tooltip';
import { Message, ReactionGroupDto } from '../../core/models';
import { formatTime } from '../../shared/dates';
import { initials } from '../../shared/initials';
import { providerIconUrl } from '../../shared/provider-icon';
import { isAttachmentUrl, MessageImages } from './attachments';
import { markMentions } from './mentions';
import { replyLabel } from './reply-label';

const QUICK_REACTIONS = ['👍', '❤️', '😂', '😮', '😢', '🎉'] as const;

/**
 * One message. Others' sit left with an avatar and, on the first of a run, name, Bot badge and
 * time; yours sit right in primary bubbles. Reply and React appear on hover and on keyboard focus.
 */
@Component({
  selector: 'app-message-item',
  imports: [
    NgIcon,
    PkMarkdown,
    HlmAvatarImports,
    HlmBadge,
    HlmButton,
    HlmPopoverImports,
    HlmTooltipImports,
    MessageImages,
  ],
  providers: [provideIcons({ lucideReply, lucideSmilePlus })],
  host: {
    class: 'group/message flex gap-3 rounded-lg transition-colors duration-500',
    '[class.flex-row-reverse]': 'own()',
    '[class.mt-4]': 'first()',
    '[class.mt-1]': '!first()',
    '[class.bg-accent]': 'highlighted()',
    '[attr.data-message-id]': 'message().id',
  },
  template: `
    @if (!own()) {
      <div class="w-8 shrink-0">
        @if (first()) {
          <hlm-avatar [class.bg-background]="brandIcon()">
            @if (avatarSrc()) {
              <img
                hlmAvatarImage
                [src]="avatarSrc()"
                alt=""
                [class.p-1.5]="brandIcon()"
                [class.object-contain]="brandIcon()"
                [class.dark:invert]="brandIcon()"
              />
            }
            <span hlmAvatarFallback class="text-xs font-medium">{{ initial() }}</span>
          </hlm-avatar>
        }
      </div>
    }

    <div class="flex min-w-0 flex-col gap-1" [class.items-end]="own()" [class.items-start]="!own()">
      @if (first()) {
        @if (own()) {
          <time class="text-muted-foreground text-xs" [attr.datetime]="message().createdAt">{{ sentAt() }}</time>
        } @else {
          <div class="flex items-center gap-1.5 text-sm leading-none">
            <span class="font-semibold">{{ message().senderName }}</span>
            @if (isBot()) {
              <span hlmBadge variant="secondary">Bot</span>
            }
            <time class="text-muted-foreground text-xs" [attr.datetime]="message().createdAt">{{ sentAt() }}</time>
          </div>
        }
      }

      @if (showReply() && message().replyTo; as reply) {
        <button
          type="button"
          class="text-muted-foreground hover:text-foreground focus-visible:ring-ring/50 flex max-w-full min-w-0 items-center gap-1 rounded-sm text-xs outline-none focus-visible:ring-2"
          [attr.aria-label]="replyLabel(reply)"
          (click)="jumpTo.emit(reply.id)"
        >
          <ng-icon name="lucideReply" class="shrink-0" />
          <span class="text-foreground shrink-0 font-medium">{{ reply.senderName }}</span>
          <span class="truncate">{{ reply.content || 'Image' }}</span>
        </button>
      }

      <div class="flex max-w-full items-center gap-1" [class.flex-row-reverse]="own()">
        <div class="flex max-w-[min(36rem,calc(100vw-7rem))] flex-col gap-1" [class.items-end]="own()" [class.items-start]="!own()">
          @if (message().attachments.length) {
            <app-message-images [attachments]="message().attachments" [senderName]="message().senderName" />
          }
          @if (message().content) {
            <div
              class="max-w-full rounded-2xl px-3 py-2 wrap-anywhere"
              [class.bg-primary]="own()"
              [class.text-primary-foreground]="own()"
              [class.bg-muted]="!own()"
            >
              <pk-markdown class="prose prose-chat block" [content]="body()" [allowImage]="allowImage" />
            </div>
          }
        </div>

        <!-- Visible on hover, and whenever focus is inside, so the keyboard can reach them. -->
        <div
          class="flex shrink-0 opacity-0 transition-opacity group-hover/message:opacity-100 focus-within:opacity-100 has-[[aria-expanded=true]]:opacity-100"
          [class.flex-row-reverse]="own()"
        >
          <button
            hlmBtn
            variant="ghost"
            size="icon-xs"
            type="button"
            aria-label="Reply"
            hlmTooltip="Reply"
            (click)="reply.emit(message())"
          >
            <ng-icon name="lucideReply" />
          </button>
          <hlm-popover [align]="own() ? 'end' : 'start'" [sideOffset]="4">
            <button
              hlmPopoverTrigger
              hlmBtn
              variant="ghost"
              size="icon-xs"
              type="button"
              aria-label="React"
            >
              <ng-icon name="lucideSmilePlus" />
            </button>
            <hlm-popover-content *hlmPopoverPortal="let ctx" class="w-auto p-1">
              <div class="flex gap-0.5" role="group" aria-label="Reactions">
                @for (emoji of quickReactions; track emoji) {
                  <button
                    hlmBtn
                    variant="ghost"
                    size="icon-sm"
                    type="button"
                    class="text-base"
                    [attr.aria-label]="'React with ' + emoji"
                    (click)="react.emit(emoji); ctx.close()"
                  >
                    {{ emoji }}
                  </button>
                }
              </div>
            </hlm-popover-content>
          </hlm-popover>
        </div>
      </div>

      @if (message().reactions.length) {
        <div class="flex flex-wrap gap-1" [class.justify-end]="own()">
          @for (reaction of message().reactions; track reaction.emoji) {
            <button
              type="button"
              class="focus-visible:ring-ring/50 inline-flex h-6 items-center gap-1 rounded-full border px-2 text-xs outline-none transition-colors focus-visible:ring-2"
              [class.bg-accent]="isMine(reaction)"
              [class.border-ring]="isMine(reaction)"
              [class.hover:bg-muted]="!isMine(reaction)"
              [attr.aria-pressed]="isMine(reaction)"
              [attr.aria-label]="reaction.emoji + ' ' + reaction.count + ': ' + reaction.names.join(', ')"
              [title]="reaction.names.join(', ')"
              (click)="react.emit(reaction.emoji)"
            >
              <span>{{ reaction.emoji }}</span>
              <span class="text-muted-foreground tabular-nums">{{ reaction.count }}</span>
            </button>
          }
        </div>
      }
    </div>
  `,
})
export class MessageItem {
  readonly message = input.required<Message>();
  readonly currentUserId = input<string | null>(null);
  /** The first of a run from one sender, which carries the name and time. */
  readonly first = input(true);
  readonly highlighted = input(false);
  /** Show the one-line reference to the replied-to message (off when it is the message right above). */
  readonly showReply = input(true);
  /** Every member's name, to mark their @mentions. */
  readonly memberNames = input<readonly string[]>([]);

  readonly reply = output<Message>();
  readonly react = output<string>();
  readonly jumpTo = output<string>();

  protected readonly quickReactions = QUICK_REACTIONS;
  protected readonly allowImage = isAttachmentUrl;
  protected readonly replyLabel = replyLabel;

  protected readonly own = computed(
    () => !!this.currentUserId() && this.message().senderUserId === this.currentUserId(),
  );
  protected readonly isBot = computed(() => this.message().senderType === 'bot');
  protected readonly initial = computed(() => initials(this.message().senderName));
  protected readonly body = computed(() =>
    markMentions(this.message().content, this.memberNames()),
  );

  /** A bot's own avatar wins; otherwise its model vendor's icon, rather than an initial. */
  protected readonly avatarSrc = computed(() => {
    const message = this.message();
    if (message.senderAvatar) return message.senderAvatar;
    return message.senderModelId ? providerIconUrl({ id: message.senderModelId }) : null;
  });
  protected readonly brandIcon = computed(
    () => !this.message().senderAvatar && !!this.message().senderModelId,
  );

  protected readonly sentAt = computed(() => {
    const date = new Date(this.message().createdAt);
    return Number.isNaN(date.getTime())
      ? ''
      : formatTime(date);
  });

  protected isMine(reaction: ReactionGroupDto): boolean {
    const me = this.currentUserId();
    return !!me && reaction.userIds.includes(me);
  }
}
