import {
  afterRenderEffect,
  ChangeDetectionStrategy,
  Component,
  computed,
  ElementRef,
  input,
  OnDestroy,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { toast } from '@spartan-ng/brain/sonner';
import { PkChatContainerImports } from '@prompt-kit/chat-container';
import { PkChatEmpty } from '@prompt-kit/chat-empty';
import { PkLoader } from '@prompt-kit/loader';
import { PkScrollButton } from '@prompt-kit/scroll-button';
import { Message } from '@app/models';
import { SystemMessage } from '@app/shared/signalr/hub-events';
import { MessageBubbleComponent } from '@app/chat/message-bubble/message-bubble';

/** Start fetching older messages this close to the top, in px. */
const LOAD_OLDER_THRESHOLD = 200;

/** How long a jumped-to message stays highlighted. */
const HIGHLIGHT_MS = 1500;

type TimelineItem =
  | { kind: 'message'; key: string; at: number; message: Message }
  | { kind: 'system'; key: string; at: number; content: string };

@Component({
  selector: 'app-message-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MessageBubbleComponent, PkChatContainerImports, PkChatEmpty, PkLoader, PkScrollButton],
  host: { class: 'flex min-h-0 flex-1 flex-col' },
  templateUrl: './message-list.html',
})
export class MessageListComponent implements OnDestroy {
  readonly messages = input.required<Message[]>();
  readonly systemMessages = input<SystemMessage[]>([]);
  readonly currentUserId = input<string | null>(null);
  readonly hasMore = input(false);
  readonly loadingOlder = input(false);

  readonly replyTo = output<Message>();
  readonly react = output<{ messageId: string; emoji: string }>();
  readonly loadOlder = output<void>();

  readonly highlightedId = signal<string | null>(null);
  private highlightTimer?: ReturnType<typeof setTimeout>;

  private readonly root = viewChild('root', { read: ElementRef<HTMLElement> });

  /**
   * System notices are ephemeral and never stored, so they are slotted into
   * the persisted history by time rather than kept in the message list.
   */
  readonly timeline = computed<TimelineItem[]>(() => {
    const items: TimelineItem[] = this.messages().map((message) => ({
      kind: 'message',
      key: message.id,
      at: Date.parse(message.createdAt),
      message,
    }));
    const system = this.systemMessages();
    if (system.length === 0) return items;
    return [
      ...items,
      ...system.map((s, i): TimelineItem => ({
        kind: 'system',
        key: `system-${i}-${s.timestamp}`,
        at: Date.parse(s.timestamp),
        content: s.content,
      })),
    ].sort((a, b) => a.at - b.at);
  });

  /** Scroll offset from the bottom as of the last render or scroll. */
  private distanceFromBottom = 0;
  private firstMessageId: string | null = null;

  constructor() {
    afterRenderEffect(() => {
      const list = this.messages();
      const el = this.root()?.nativeElement;
      if (!el) return;

      // Older messages were prepended: keep what the reader was looking at
      // where it was, instead of letting the new content push it down.
      const first = list[0]?.id ?? null;
      if (this.firstMessageId && first !== this.firstMessageId && list.some((m) => m.id === this.firstMessageId)) {
        el.scrollTop = el.scrollHeight - el.clientHeight - this.distanceFromBottom;
      }
      this.firstMessageId = first;
      this.measure(el);

      // A page that does not fill the viewport can never be scrolled up.
      if (this.hasMore() && !this.loadingOlder() && el.scrollHeight <= el.clientHeight) {
        this.loadOlder.emit();
      }
    });
  }

  ngOnDestroy(): void {
    clearTimeout(this.highlightTimer);
  }

  onScroll(): void {
    const el = this.root()?.nativeElement;
    if (!el) return;
    this.measure(el);
    if (el.scrollTop < LOAD_OLDER_THRESHOLD && this.hasMore() && !this.loadingOlder()) {
      this.loadOlder.emit();
    }
  }

  onReply(message: Message): void {
    this.replyTo.emit(message);
  }

  onReact(event: { messageId: string; emoji: string }): void {
    this.react.emit(event);
  }

  jumpTo(messageId: string): void {
    const target = this.root()?.nativeElement.querySelector(`[data-message-id="${CSS.escape(messageId)}"]`);
    if (!target) {
      toast.info('The original message is further back. Scroll up to load it.');
      return;
    }
    target.scrollIntoView({ behavior: 'smooth', block: 'center' });
    clearTimeout(this.highlightTimer);
    this.highlightedId.set(messageId);
    this.highlightTimer = setTimeout(() => this.highlightedId.set(null), HIGHLIGHT_MS);
  }

  private measure(el: HTMLElement): void {
    this.distanceFromBottom = el.scrollHeight - el.scrollTop - el.clientHeight;
  }
}
