import { ChangeDetectionStrategy, Component, computed, input, output, signal } from '@angular/core';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmAvatar, HlmAvatarFallback } from '@spartan-ng/helm/avatar';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideX, lucideSendHorizontal } from '@ng-icons/lucide';
import { PkPromptInputImports } from '@prompt-kit/prompt-input';
import { ChatMember, Message } from '@app/models';

/** Matches a trailing "@word" that the caret is still sitting inside. */
const TRAILING_MENTION = /@(\w*)$/;

@Component({
  selector: 'app-message-input',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PkPromptInputImports, HlmButton, HlmAvatar, HlmAvatarFallback, NgIcon],
  viewProviders: [provideIcons({ lucideX, lucideSendHorizontal })],
  templateUrl: './message-input.html',
})
export class MessageInputComponent {
  readonly members = input.required<ChatMember[]>();
  readonly replyingTo = input<Message | null>(null);

  readonly send = output<{ content: string; mentions: string[] }>();
  readonly cancelReply = output<void>();

  readonly content = signal('');

  /**
   * The partial name after a trailing "@". Derived from the content rather than
   * from keystrokes, so it stays correct for paste and autosize edits too.
   */
  readonly mentionQuery = computed(() => {
    const match = TRAILING_MENTION.exec(this.content());
    return match ? match[1] : null;
  });

  readonly mentionSuggestions = computed(() => {
    const query = this.mentionQuery();
    if (query === null) return [];
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

  onSelectMention(member: ChatMember): void {
    const name = member.displayName;
    if (!name) return;
    this.content.update((text) => text.replace(TRAILING_MENTION, `@${name} `));
  }

  onCancelReply(): void {
    this.cancelReply.emit();
  }
}
