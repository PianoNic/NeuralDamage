import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmAvatar, HlmAvatarFallback, HlmAvatarImage } from '@spartan-ng/helm/avatar';
import { HlmBadge } from '@spartan-ng/helm/badge';
import { Message } from '@app/models';
import { ReactionBarComponent } from '@app/chat/reaction-bar/reaction-bar';

@Component({
  selector: 'app-message-bubble',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [HlmButton, HlmAvatar, HlmAvatarFallback, HlmAvatarImage, HlmBadge, ReactionBarComponent],
  templateUrl: './message-bubble.html',
})
export class MessageBubbleComponent {
  readonly message = input.required<Message>();
  readonly reply = output<Message>();

  readonly senderInitial = computed(() => this.message().senderName.charAt(0).toUpperCase());
  readonly isBot = computed(() => this.message().senderType === 'bot');
  readonly hasReactions = computed(() => this.message().reactions.length > 0);
  readonly hasReply = computed(() => this.message().replyTo !== null);

  onReply(): void {
    this.reply.emit(this.message());
  }

  toggleReaction(emoji: string): void {
    // TODO: implement via SignalR
  }
}
