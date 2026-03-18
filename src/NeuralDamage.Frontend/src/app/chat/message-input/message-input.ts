import { ChangeDetectionStrategy, Component, computed, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmTextarea } from '@spartan-ng/helm/textarea';
import { HlmCard } from '@spartan-ng/helm/card';
import { ChatMember, Message } from '@app/models';

@Component({
  selector: 'app-message-input',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, HlmButton, HlmTextarea, HlmCard],
  templateUrl: './message-input.html',
})
export class MessageInputComponent {
  readonly members = input.required<ChatMember[]>();
  readonly replyingTo = input<Message | null>(null);

  readonly send = output<{ content: string; mentions: string[] }>();
  readonly cancelReply = output<void>();

  readonly content = signal('');
  readonly mentionQuery = signal<string | null>(null);

  readonly mentionSuggestions = computed(() => {
    const query = this.mentionQuery();
    if (!query) return [];
    return this.members().filter((m) =>
      m.displayName?.toLowerCase().includes(query.toLowerCase()),
    );
  });

  onSend(): void {
    const text = this.content().trim();
    if (!text) return;
    // TODO: extract mentions from content
    this.send.emit({ content: text, mentions: [] });
    this.content.set('');
  }

  onCancelReply(): void {
    this.cancelReply.emit();
  }

  onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault();
      this.onSend();
    }
  }
}
