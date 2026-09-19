import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { PkChatContainerImports } from '@prompt-kit/chat-container';
import { PkChatEmpty } from '@prompt-kit/chat-empty';
import { PkScrollButton } from '@prompt-kit/scroll-button';
import { Message } from '@app/models';
import { MessageBubbleComponent } from '@app/chat/message-bubble/message-bubble';

@Component({
  selector: 'app-message-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MessageBubbleComponent, PkChatContainerImports, PkChatEmpty, PkScrollButton],
  host: { class: 'flex min-h-0 flex-1 flex-col' },
  templateUrl: './message-list.html',
})
export class MessageListComponent {
  readonly messages = input.required<Message[]>();
  readonly replyTo = output<Message>();
  readonly react = output<{ messageId: string; emoji: string }>();

  onReply(message: Message): void {
    this.replyTo.emit(message);
  }

  onReact(event: { messageId: string; emoji: string }): void {
    this.react.emit(event);
  }
}
