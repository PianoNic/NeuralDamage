import {
  afterRenderEffect,
  Component,
  computed,
  ElementRef,
  input,
  OnDestroy,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { PkChatContainerImports, PkChatContainerRoot } from '@prompt-kit/chat-container';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmEmptyImports } from '@spartan-ng/helm/empty';
import { HlmSpinner } from '@spartan-ng/helm/spinner';
import { Message } from '../../core/models';
import { SystemMessage } from '../../core/signalr/hub-events';
import { MessageItem } from './message-item';

/** Start fetching older messages this close to the top, in px. */
const LOAD_OLDER_THRESHOLD = 200;

/** How long a jumped-to message stays highlighted. */
const HIGHLIGHT_MS = 1500;

/** Messages from one sender this close together form one run under one name. */
const GROUP_GAP_MS = 5 * 60 * 1000;

export type TimelineItem =
  | { kind: 'message'; key: string; at: number; message: Message; first: boolean; showReply: boolean }
  | { kind: 'system'; key: string; at: number; content: string }
  | { kind: 'day'; key: string; at: number; label: string };

/**
 * Persisted messages and ephemeral system notices slotted in by time, day separators between days,
 * and each message flagged as the first of its sender's run or not. The one-line reply reference
 * is only shown when the reply target is not the message right above, where it would be noise.
 */
export function buildTimeline(
  messages: readonly Message[],
  system: readonly SystemMessage[],
  now: Date,
): TimelineItem[] {
  const entries = [
    ...messages.map((message) => ({
      kind: 'message' as const,
      key: message.id,
      at: Date.parse(message.createdAt),
      message,
    })),
    ...system.map((s, i) => ({
      kind: 'system' as const,
      key: `system-${i}-${s.timestamp}`,
      at: Date.parse(s.timestamp),
      content: s.content,
    })),
  ].sort((a, b) => a.at - b.at);

  const items: TimelineItem[] = [];
  let lastDay = '';
  let previous = null as TimelineItem | null;
  let previousMessageId: string | null = null;
  for (const entry of entries) {
    const day = new Date(entry.at).toDateString();
    if (day !== lastDay) {
      items.push({ kind: 'day', key: `day-${day}`, at: entry.at, label: dayLabel(new Date(entry.at), now) });
      lastDay = day;
      previous = null;
    }
    if (entry.kind === 'message') {
      const first: boolean = !(
        previous?.kind === 'message' &&
        senderOf(previous.message) === senderOf(entry.message) &&
        entry.at - previous.at < GROUP_GAP_MS
      );
      const replyToId = entry.message.replyTo?.id ?? null;
      const showReply = !!replyToId && replyToId !== previousMessageId;
      previousMessageId = entry.message.id;
      previous = { ...entry, first, showReply };
    } else {
      previous = entry;
    }
    items.push(previous);
  }
  return items;
}

function senderOf(message: Message): string {
  return message.senderBotId ?? message.senderUserId ?? message.senderName;
}

function dayLabel(date: Date, now: Date): string {
  const startOf = (d: Date) => new Date(d.getFullYear(), d.getMonth(), d.getDate()).getTime();
  const days = Math.round((startOf(now) - startOf(date)) / 86_400_000);
  if (days === 0) return 'Today';
  if (days === 1) return 'Yesterday';
  return date.toLocaleDateString(undefined, {
    weekday: 'long',
    day: 'numeric',
    month: 'long',
    year: date.getFullYear() === now.getFullYear() ? undefined : 'numeric',
  });
}

@Component({
  selector: 'app-message-list',
  imports: [PkChatContainerImports, HlmEmptyImports, HlmSpinner, MessageItem],
  host: { class: 'flex min-h-0 flex-1 flex-col' },
  template: `
    @if (timeline().length === 0) {
      <hlm-empty class="flex-1">
        <hlm-empty-header>
          <h2 hlmEmptyTitle>No messages yet</h2>
          <p hlmEmptyDescription>
            Say something, or mention a bot with &#64; to pull it into the conversation.
          </p>
        </hlm-empty-header>
      </hlm-empty>
    } @else {
      <pk-chat-container-root class="min-h-0 flex-1" (scroll)="onScroll()">
        <pk-chat-container-content class="mx-auto w-full max-w-3xl px-4 pt-4 pb-2">
          @if (loadingOlder()) {
            <div class="flex justify-center py-2"><hlm-spinner /></div>
          }
          @for (item of timeline(); track item.key) {
            @switch (item.kind) {
              @case ('message') {
                <app-message-item
                  [message]="item.message"
                  [first]="item.first"
                  [showReply]="item.showReply"
                  [currentUserId]="currentUserId()"
                  [memberNames]="memberNames()"
                  [highlighted]="highlightedId() === item.message.id"
                  (reply)="replyTo.emit($event)"
                  (react)="react.emit({ messageId: item.message.id, emoji: $event })"
                  (jumpTo)="jumpTo($event)"
                />
              }
              @case ('system') {
                <div class="my-2 flex justify-center">
                  <span
                    role="status"
                    class="bg-muted text-muted-foreground max-w-[85%] rounded-full px-3 py-1 text-center text-xs whitespace-pre-wrap"
                    >{{ item.content }}</span
                  >
                </div>
              }
              @case ('day') {
                <div class="text-muted-foreground my-3 flex items-center gap-3 text-xs" role="separator">
                  <span class="bg-border h-px flex-1"></span>
                  {{ item.label }}
                  <span class="bg-border h-px flex-1"></span>
                </div>
              }
            }
          }
        </pk-chat-container-content>
        <pk-chat-container-scroll-anchor />
      </pk-chat-container-root>
    }
  `,
})
export class MessageList implements OnDestroy {
  readonly messages = input.required<Message[]>();
  readonly systemMessages = input<SystemMessage[]>([]);
  readonly currentUserId = input<string | null>(null);
  readonly memberNames = input<readonly string[]>([]);
  readonly hasMore = input(false);
  readonly loadingOlder = input(false);

  readonly replyTo = output<Message>();
  readonly react = output<{ messageId: string; emoji: string }>();
  readonly loadOlder = output<void>();

  readonly highlightedId = signal<string | null>(null);
  private highlightTimer?: ReturnType<typeof setTimeout>;

  private readonly container = viewChild(PkChatContainerRoot);
  private readonly root = viewChild(PkChatContainerRoot, { read: ElementRef<HTMLElement> });

  readonly timeline = computed(() =>
    buildTimeline(this.messages(), this.systemMessages(), new Date()),
  );

  /** Scroll offset from the bottom as of the last render or scroll. */
  private distanceFromBottom = 0;
  private firstMessageId: string | null = null;

  constructor() {
    afterRenderEffect(() => {
      const list = this.messages();
      const el = this.root()?.nativeElement;
      if (!el) return;

      // Older messages were prepended: keep what the reader was looking at where it was, instead
      // of letting the new content push it down.
      const first = list[0]?.id ?? null;
      if (
        this.firstMessageId &&
        first !== this.firstMessageId &&
        list.some((m) => m.id === this.firstMessageId)
      ) {
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

  /** Brings the newest message into view, e.g. after sending. */
  scrollToBottom(): void {
    this.container()?.scrollToBottom('auto');
  }

  protected onScroll(): void {
    const el = this.root()?.nativeElement;
    if (!el) return;
    this.measure(el);
    if (el.scrollTop < LOAD_OLDER_THRESHOLD && this.hasMore() && !this.loadingOlder()) {
      this.loadOlder.emit();
    }
  }

  protected jumpTo(messageId: string): void {
    const target = this.root()?.nativeElement.querySelector(
      `[data-message-id="${CSS.escape(messageId)}"]`,
    );
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
