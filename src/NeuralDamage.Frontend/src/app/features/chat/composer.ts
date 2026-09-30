import {
  afterNextRender,
  afterRenderEffect,
  Component,
  computed,
  effect,
  ElementRef,
  inject,
  Injector,
  input,
  linkedSignal,
  output,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideArrowUp,
  lucideAtSign,
  lucidePlus,
  lucideReply,
  lucideSmile,
  lucideX,
} from '@ng-icons/lucide';
import { HlmAvatarImports } from '@spartan-ng/helm/avatar';
import { HlmBadge } from '@spartan-ng/helm/badge';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmPopoverImports } from '@spartan-ng/helm/popover';
import { HlmTooltipImports } from '@spartan-ng/helm/tooltip';
import { ChatMember, Message } from '../../core/models';
import { initials } from '../../shared/initials';

/** The server's limit on a message body (SendMessageValidator). */
export const MAX_MESSAGE_LENGTH = 4000;

/** The counter shows from here, so the limit is no surprise. */
const COUNTER_FROM = MAX_MESSAGE_LENGTH - 500;

/** Matches a trailing "@word" that the caret is still sitting inside. */
const TRAILING_MENTION = /(^|\s)@(\w*)$/;

/** A command still being typed: "/" and no space yet. */
const PARTIAL_COMMAND = /^\/(\S*)$/;

/** A command that takes a bot, with its argument being typed. */
const BOT_ARGUMENT = /^\/(mute|unmute|kick) (.*)$/i;

/**
 * The server parses these out of an ordinary message; the list here only drives autocomplete.
 * `arg` means the command takes an argument, so picking it leaves a trailing space to type into.
 */
export const SLASH_COMMANDS = [
  { command: '/stop', description: 'Stop all bot responses', arg: null },
  { command: '/mute', description: 'Keep a bot quiet in this chat', arg: '<bot>' },
  { command: '/unmute', description: 'Let a muted bot talk again', arg: '<bot>' },
  { command: '/clear', description: 'Clear all messages in this chat', arg: null },
  { command: '/kick', description: 'Remove a bot from this chat', arg: '<bot>' },
  { command: '/rename', description: 'Rename this chat', arg: '<name>' },
  { command: '/bots', description: 'List the bots in this chat', arg: null },
  { command: '/help', description: 'Show available commands', arg: null },
] as const;

const QUICK_EMOJIS = ['😂', '👍', '❤️', '🔥', '😮', '😢', '🎉', '🤔', '👀', '💀', '🍕', '🙏'] as const;

export interface Suggestion {
  key: string;
  kind: 'command' | 'member';
  label: string;
  description?: string;
  member?: ChatMember;
  /** The whole composer text once this suggestion is picked. */
  result: string;
}

/**
 * The message box: text on top, then attach / mention / emoji and a round send button. Slash
 * commands and @mentions complete in a Command-style popover above it, driven from the keyboard.
 */
@Component({
  selector: 'app-composer',
  imports: [NgIcon, HlmAvatarImports, HlmBadge, HlmButton, HlmPopoverImports, HlmTooltipImports],
  providers: [
    provideIcons({
      lucideArrowUp,
      lucideAtSign,
      lucidePlus,
      lucideReply,
      lucideSmile,
      lucideX,
    }),
  ],
  host: { class: 'relative block' },
  template: `
    @if (suggestions().length > 0) {
      <div
        data-slot="command"
        class="bg-popover text-popover-foreground ring-foreground/10 drop-in absolute bottom-full start-0 z-20 mb-2 w-full max-w-sm overflow-hidden rounded-xl p-1 shadow-md ring-1"
      >
        <div
          [id]="listId"
          role="listbox"
          [attr.aria-label]="suggestionLabel()"
          class="max-h-72 overflow-y-auto"
        >
          <div class="text-muted-foreground px-2 py-1.5 text-xs font-medium" role="presentation">
            {{ suggestionLabel() }}
          </div>
          @for (item of suggestions(); track item.key; let i = $index) {
            <button
              type="button"
              role="option"
              tabindex="-1"
              [id]="listId + '-' + i"
              class="data-selected:bg-muted data-selected:text-foreground flex w-full cursor-default items-center gap-2 rounded-sm px-2 py-1.5 text-start text-sm outline-hidden select-none"
              [attr.data-selected]="i === activeIndex() ? true : null"
              [attr.aria-selected]="i === activeIndex()"
              (mousedown)="$event.preventDefault()"
              (mouseenter)="activeIndex.set(i)"
              (click)="pick(item)"
            >
              @if (item.kind === 'command') {
                <span class="font-medium">{{ item.label }}</span>
                <span class="text-muted-foreground truncate">{{ item.description }}</span>
              } @else {
                <hlm-avatar size="sm">
                  @if (item.member?.avatarUrl) {
                    <img hlmAvatarImage [src]="item.member!.avatarUrl" alt="" />
                  }
                  <span hlmAvatarFallback class="text-[10px]">{{ initialsOf(item.label) }}</span>
                </hlm-avatar>
                <span class="truncate">{{ item.label }}</span>
                @if (item.member?.memberType === 'bot') {
                  <span hlmBadge variant="secondary" class="ms-auto">Bot</span>
                }
              }
            </button>
          }
        </div>
      </div>
    }

    <div
      class="border-input bg-background focus-within:border-ring flex cursor-text flex-col gap-1 rounded-2xl border px-3 pt-2.5 pb-2 shadow-xs transition-colors"
      (click)="focusFromChrome($event)"
    >
      @if (replyingTo(); as reply) {
        <div class="bg-muted flex items-center gap-2 rounded-lg py-1 ps-2 pe-1 text-xs">
          <ng-icon name="lucideReply" class="text-muted-foreground shrink-0" />
          <span class="min-w-0 flex-1 truncate">
            <span class="text-muted-foreground">Replying to </span>
            <span class="font-medium">{{ reply.senderName }}</span>
            <span class="text-muted-foreground">: {{ reply.content }}</span>
          </span>
          <button
            hlmBtn
            variant="ghost"
            size="icon-xs"
            type="button"
            aria-label="Cancel reply (Esc)"
            (click)="cancelReply.emit()"
          >
            <ng-icon name="lucideX" />
          </button>
        </div>
      }

      <label class="sr-only" [for]="textareaId">{{ placeholder() }}</label>
      <textarea
        #textarea
        rows="1"
        [id]="textareaId"
        [value]="content()"
        [placeholder]="placeholder()"
        role="combobox"
        aria-autocomplete="list"
        [attr.aria-expanded]="suggestions().length > 0"
        [attr.aria-controls]="suggestions().length > 0 ? listId : null"
        [attr.aria-activedescendant]="suggestions().length > 0 ? listId + '-' + activeIndex() : null"
        [attr.aria-invalid]="tooLong() || null"
        [attr.aria-describedby]="showCounter() ? counterId : null"
        class="placeholder:text-muted-foreground max-h-50 min-h-7 w-full resize-none bg-transparent px-1 py-1 text-sm leading-normal outline-none"
        (input)="onValueChange($any($event.target).value)"
        (keydown)="onKeyDown($event)"
      ></textarea>

      <div class="flex items-center gap-0.5">
        <button
          hlmBtn
          variant="ghost"
          size="icon-sm"
          type="button"
          aria-label="Commands"
          hlmTooltip="Commands"
          (click)="insertTrigger('/')"
        >
          <ng-icon name="lucidePlus" />
        </button>
        <button
          hlmBtn
          variant="ghost"
          size="icon-sm"
          type="button"
          aria-label="Mention someone"
          hlmTooltip="Mention"
          (click)="insertTrigger('@')"
        >
          <ng-icon name="lucideAtSign" />
        </button>
        <hlm-popover align="start" [sideOffset]="8">
          <button
            hlmPopoverTrigger
            hlmBtn
            variant="ghost"
            size="icon-sm"
            type="button"
            aria-label="Insert emoji"
          >
            <ng-icon name="lucideSmile" />
          </button>
          <hlm-popover-content *hlmPopoverPortal="let ctx" class="w-auto p-1.5">
            <div class="grid grid-cols-6 gap-0.5" role="group" aria-label="Emoji">
              @for (emoji of emojis; track emoji) {
                <button
                  hlmBtn
                  variant="ghost"
                  size="icon-sm"
                  type="button"
                  class="text-base"
                  [attr.aria-label]="'Insert ' + emoji"
                  (click)="insertEmoji(emoji); ctx.close()"
                >
                  {{ emoji }}
                </button>
              }
            </div>
          </hlm-popover-content>
        </hlm-popover>

        <span class="flex-1"></span>

        @if (showCounter()) {
          <span
            [id]="counterId"
            class="me-2 text-xs tabular-nums"
            [class.text-destructive]="tooLong()"
            [class.text-muted-foreground]="!tooLong()"
          >
            {{ content().length }} / {{ maxLength }}
            @if (tooLong()) {
              <span class="sr-only">Message is too long</span>
            }
          </span>
        }

        <button
          hlmBtn
          size="icon-sm"
          type="button"
          class="rounded-full"
          aria-label="Send message"
          [disabled]="!canSend()"
          (click)="onSend()"
        >
          <ng-icon name="lucideArrowUp" />
        </button>
      </div>
    </div>
  `,
})
export class Composer {
  private readonly injector = inject(Injector);
  private readonly textarea = viewChild.required<ElementRef<HTMLTextAreaElement>>('textarea');

  readonly members = input.required<ChatMember[]>();
  readonly replyingTo = input<Message | null>(null);
  readonly currentUserId = input<string | null>(null);
  readonly placeholder = input('Message');

  readonly send = output<{ content: string; mentions: string[] }>();
  readonly typing = output<void>();
  readonly cancelReply = output<void>();

  private static nextId = 0;
  protected readonly textareaId = `composer-${Composer.nextId++}`;
  protected readonly listId = `${this.textareaId}-suggestions`;
  protected readonly counterId = `${this.textareaId}-counter`;
  protected readonly maxLength = MAX_MESSAGE_LENGTH;
  protected readonly emojis = QUICK_EMOJIS;

  readonly content = signal('');

  protected readonly tooLong = computed(() => this.content().length > MAX_MESSAGE_LENGTH);
  protected readonly showCounter = computed(() => this.content().length > COUNTER_FROM);
  protected readonly canSend = computed(() => !!this.content().trim() && !this.tooLong());

  /** The text Esc closed the popup on; it reopens once the text changes. */
  private readonly dismissedAt = signal<string | null>(null);

  /** Everyone who can be mentioned: the chat's members, without yourself. */
  private readonly others = computed(() => {
    const me = this.currentUserId();
    return this.members().filter((m) => !me || m.userId !== me);
  });

  /**
   * Derived from the content rather than from keystrokes, so it stays correct for paste and
   * programmatic edits too.
   */
  readonly suggestions = computed<Suggestion[]>(() => {
    const text = this.content();
    if (text === this.dismissedAt()) return [];

    const partial = PARTIAL_COMMAND.exec(text);
    if (partial) {
      const query = `/${partial[1].toLowerCase()}`;
      return SLASH_COMMANDS.filter((c) => c.command.startsWith(query)).map((c) => ({
        key: c.command,
        kind: 'command' as const,
        label: c.arg ? `${c.command} ${c.arg}` : c.command,
        description: c.description,
        result: c.arg ? `${c.command} ` : c.command,
      }));
    }

    const botArg = BOT_ARGUMENT.exec(text);
    if (botArg) {
      const command = botArg[1].toLowerCase();
      const query = botArg[2].toLowerCase();
      return this.members()
        .filter((m) => m.memberType === 'bot' && m.displayName.toLowerCase().includes(query))
        .map((m) => ({
          key: m.id,
          kind: 'member' as const,
          label: m.displayName,
          member: m,
          result: `/${command} ${m.displayName}`,
        }));
    }

    const mention = TRAILING_MENTION.exec(text);
    if (mention) {
      const query = mention[2].toLowerCase();
      return this.others()
        .filter((m) => m.displayName?.toLowerCase().includes(query))
        .map((m) => ({
          key: m.id,
          kind: 'member' as const,
          label: m.displayName,
          member: m,
          result: text.replace(TRAILING_MENTION, `$1@${m.displayName} `),
        }));
    }

    return [];
  });

  protected readonly suggestionLabel = computed(() =>
    this.suggestions()[0]?.kind === 'command' ? 'Commands' : 'Members',
  );

  /** Keyboard selection; back to the top whenever the list changes. */
  readonly activeIndex = linkedSignal({ source: this.suggestions, computation: () => 0 });

  constructor() {
    // Focus the box as soon as a reply starts.
    effect(() => {
      if (this.replyingTo()) untracked(() => this.focusInput());
    });

    // Grow with the content, up to the max-height the class sets.
    afterRenderEffect(() => {
      this.content();
      const el = this.textarea().nativeElement;
      el.style.height = 'auto';
      el.style.height = `${el.scrollHeight}px`;
    });
  }

  onValueChange(value: string): void {
    this.content.set(value);
    if (value.trim()) this.typing.emit();
  }

  onSend(): void {
    const text = this.content().trim();
    if (!text || this.tooLong()) return;
    this.send.emit({ content: text, mentions: [] });
    this.content.set('');
    // If Enter lands before the last keystroke has rendered, the [value] binding sees '' before
    // and after and leaves the sent text in the box, so clear the box directly.
    this.textarea().nativeElement.value = '';
  }

  pick(suggestion: Suggestion): void {
    this.content.set(suggestion.result);
    this.focusInput();
  }

  focus(): void {
    this.focusInput();
  }

  protected insertTrigger(trigger: '/' | '@'): void {
    const text = this.content();
    if (trigger === '/') this.content.set(text.startsWith('/') ? text : '/');
    else this.content.set(text && !/\s$/.test(text) ? `${text} @` : `${text}@`);
    this.dismissedAt.set(null);
    this.focusInput();
  }

  protected insertEmoji(emoji: string): void {
    const el = this.textarea().nativeElement;
    const text = this.content();
    const at = el.selectionStart ?? text.length;
    this.content.set(text.slice(0, at) + emoji + text.slice(el.selectionEnd ?? at));
    this.focusInput(at + emoji.length);
  }

  protected initialsOf(name: string): string {
    return initials(name);
  }

  protected onKeyDown(event: KeyboardEvent): void {
    const suggestions = this.suggestions();

    if (suggestions.length > 0) {
      const index = this.activeIndex();
      const current = suggestions[index];
      switch (event.key) {
        case 'ArrowDown':
          event.preventDefault();
          this.activeIndex.set((index + 1) % suggestions.length);
          return;
        case 'ArrowUp':
          event.preventDefault();
          this.activeIndex.set((index - 1 + suggestions.length) % suggestions.length);
          return;
        case 'Tab':
          event.preventDefault();
          this.pick(current);
          return;
        case 'Enter':
          // Once the text already is the pick (e.g. "/stop"), Enter sends it.
          if (event.shiftKey || this.content().trim() === current.result.trim()) break;
          event.preventDefault();
          this.pick(current);
          return;
        case 'Escape':
          event.preventDefault();
          event.stopPropagation();
          this.dismissedAt.set(this.content());
          return;
      }
    }

    if (event.key === 'Escape' && this.replyingTo()) {
      event.preventDefault();
      event.stopPropagation();
      this.cancelReply.emit();
      return;
    }

    if (event.key === 'Enter' && !event.shiftKey && !event.isComposing) {
      event.preventDefault();
      this.onSend();
    }
  }

  /** A click on the box's padding focuses the text, like a click inside it. */
  protected focusFromChrome(event: MouseEvent): void {
    if (event.target === event.currentTarget) this.focusInput();
  }

  /** Focus the textarea with the caret at `caret` (default: the end), after the value has rendered. */
  private focusInput(caret?: number): void {
    afterNextRender(
      () => {
        const el = this.textarea().nativeElement;
        el.focus();
        const at = caret ?? el.value.length;
        el.setSelectionRange(at, at);
      },
      { injector: this.injector },
    );
  }
}
