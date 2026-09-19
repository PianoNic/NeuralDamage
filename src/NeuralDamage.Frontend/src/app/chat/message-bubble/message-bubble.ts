import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmAvatar, HlmAvatarFallback, HlmAvatarImage } from '@spartan-ng/helm/avatar';
import { HlmBadge } from '@spartan-ng/helm/badge';
import { HlmBubbleImports } from '@spartan-ng/helm/bubble';
import { HlmDropdownMenuImports } from '@spartan-ng/helm/dropdown-menu';
import { HlmMessageImports } from '@spartan-ng/helm/message';
import { Message } from '@app/models';
import { ReactionBarComponent } from '@app/chat/reaction-bar/reaction-bar';
import { PkMessageContent } from '@prompt-kit/message';
import { modelIconUrl } from '@prompt-kit/model-icon';

@Component({
  selector: 'app-message-bubble',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    HlmButton,
    HlmAvatar,
    HlmAvatarFallback,
    HlmAvatarImage,
    HlmBadge,
    ReactionBarComponent,
    PkMessageContent,
    HlmBubbleImports,
    HlmDropdownMenuImports,
    HlmMessageImports,
  ],
  host: { class: 'contents' },
  templateUrl: './message-bubble.html',
})
export class MessageBubbleComponent {
  readonly message = input.required<Message>();
  readonly reply = output<Message>();
  readonly react = output<{ messageId: string; emoji: string }>();

  /** Quick picks, same set the legacy UI offered. */
  readonly quickEmojis = ['👍', '❤️', '😂', '😮', '😢', '🎉'] as const;

  readonly senderInitial = computed(() => this.message().senderName.charAt(0).toUpperCase());
  readonly isBot = computed(() => this.message().senderType === 'bot');
  readonly hasReactions = computed(() => this.message().reactions.length > 0);
  readonly hasReply = computed(() => this.message().replyTo !== null);

  /**
   * A bot's own avatar wins; otherwise fall back to the model vendor's brand
   * icon rather than an initial, which is what Polyglot does.
   */
  readonly avatarSrc = computed(() => {
    const message = this.message();
    if (message.senderAvatar) return message.senderAvatar;
    return message.senderModelId ? modelIconUrl({ id: message.senderModelId }) : null;
  });

  /** The vendor glyph is monochrome, so it needs inverting in dark mode. */
  readonly avatarIsBrandIcon = computed(
    () => !this.message().senderAvatar && !!this.message().senderModelId,
  );

  /** Wall-clock time; the API sends an ISO string. */
  readonly sentAt = computed(() => {
    const raw = this.message().createdAt;
    if (!raw) return '';
    const date = new Date(raw);
    return Number.isNaN(date.getTime())
      ? ''
      : date.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' });
  });

  onReply(): void {
    this.reply.emit(this.message());
  }

  toggleReaction(emoji: string): void {
    this.react.emit({ messageId: this.message().id, emoji });
  }
}
