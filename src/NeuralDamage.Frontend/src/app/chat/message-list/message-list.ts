import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideMessageSquare } from '@ng-icons/lucide';
import { Message } from '@app/models';
import { MessageBubbleComponent } from '@app/chat/message-bubble/message-bubble';

@Component({
  selector: 'app-message-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MessageBubbleComponent, NgIcon],
  viewProviders: [provideIcons({ lucideMessageSquare })],
  templateUrl: './message-list.html',
})
export class MessageListComponent {
  readonly messages = input.required<Message[]>();
  readonly replyTo = output<Message>();

  onReply(message: Message): void {
    this.replyTo.emit(message);
  }
}
