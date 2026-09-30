import {
  afterNextRender,
  ChangeDetectionStrategy,
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
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmAvatar, HlmAvatarFallback, HlmAvatarImage } from '@spartan-ng/helm/avatar';
import { HlmBadge } from '@spartan-ng/helm/badge';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideArrowUp, lucideSquareTerminal, lucideX } from '@ng-icons/lucide';
import { PkPromptInput, PkPromptInputImports } from '@prompt-kit/prompt-input';
import { ChatMember, Message } from '@app/models';

/** Matches a trailing "@word" that the caret is still sitting inside. */
const TRAILING_MENTION = /(^|\s)@(\w*)$/;

/** A command still being typed: "/" and no space yet. */
const PARTIAL_COMMAND = /^\/(\S*)$/;

/** A command that takes a bot, with its argument being typed. */
const BOT_ARGUMENT = /^\/(mute|unmute|kick) (.*)$/i;

/**
 * The server parses these out of an ordinary message; the list here only
 * drives autocomplete. `arg` means the command takes an argument, so picking it
 * leaves a trailing space to type into.
 */
export const SLASH_COMMANDS = [
  { command: '/stop', description: 'Stop all bot responses', arg: null },
  { command: '/mute', description: 'Mute a bot, or all bots', arg: '<bot>' },
  { command: '/unmute', description: 'Unmute a bot, or all bots', arg: '<bot>' },
  { command: '/clear', description: 'Clear all messages in this chat', arg: null },
  { command: '/kick', description: 'Remove a bot from this chat', arg: '<bot>' },
  { command: '/rename', description: 'Rename this chat', arg: '<name>' },
  { command: '/bots', description: 'List the bots in this chat', arg: null },
  { command: '/help', description: 'Show available commands', arg: null },
] as const;

export interface Suggestion {
  key: string;
  kind: 'command' | 'member';
  label: string;
  description?: string;
  member?: ChatMember;
  /** The whole composer text once this suggestion is picked. */
  result: string;
}

@Component({
  selector: 'app-message-input',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PkPromptInputImports, HlmButton, HlmAvatar, HlmAvatarFallback, HlmAvatarImage, HlmBadge, NgIcon],
  viewProviders: [provideIcons({ lucideArrowUp, lucideSquareTerminal, lucideX })],
  templateUrl: './message-input.html',
})
export class MessageInputComponent {
  private readonly injector = inject(Injector);
  private readonly promptInput = viewChild(PkPromptInput);
  private readonly composer = viewChild<ElementRef<HTMLElement>>('composer');

  readonly members = input.required<ChatMember[]>();
  readonly replyingTo = input<Message | null>(null);

  readonly send = output<{ content: string; mentions: string[] }>();
  readonly typing = output<void>();
  readonly cancelReply = output<void>();

  readonly content = signal('');

  /** The text Esc closed the popup on; it reopens once the text changes. */
  private readonly dismissedAt = signal<string | null>(null);

  /**
   * Derived from the content rather than from keystrokes, so it stays correct
   * for paste and autosize edits too.
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
      return this.members()
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

  /** Keyboard selection; back to the top whenever the list changes. */
  readonly activeIndex = linkedSignal({ source: this.suggestions, computation: () => 0 });

  constructor() {
    // Focus the composer as soon as a reply starts.
    effect(() => {
      if (this.replyingTo()) untracked(() => this.focusInput());
    });

    // The textarea's own keydown submits on Enter before anything bubbles up,
    // so the popup's keys have to be claimed on the way down.
    afterNextRender(() => {
      this.composer()?.nativeElement.addEventListener('keydown', (e) => this.onKeyDown(e), true);
    });
  }

  onValueChange(value: string): void {
    this.content.set(value);
    if (value.trim()) this.typing.emit();
  }

  onSend(): void {
    const text = this.content().trim();
    if (!text) return;
    // TODO: extract mentions from content
    this.send.emit({ content: text, mentions: [] });
    this.content.set('');
  }

  pick(suggestion: Suggestion): void {
    this.content.set(suggestion.result);
    this.focusInput();
  }

  onCancelReply(): void {
    this.cancelReply.emit();
  }

  private onKeyDown(event: KeyboardEvent): void {
    const suggestions = this.suggestions();

    if (suggestions.length > 0) {
      const index = this.activeIndex();
      const current = suggestions[index];
      switch (event.key) {
        case 'ArrowDown':
          this.claim(event);
          this.activeIndex.set((index + 1) % suggestions.length);
          return;
        case 'ArrowUp':
          this.claim(event);
          this.activeIndex.set((index - 1 + suggestions.length) % suggestions.length);
          return;
        case 'Tab':
          this.claim(event);
          this.pick(current);
          return;
        case 'Enter':
          // Once the text already is the pick (e.g. "/stop"), Enter sends it.
          if (event.shiftKey || this.content().trim() === current.result.trim()) break;
          this.claim(event);
          this.pick(current);
          return;
        case 'Escape':
          this.claim(event);
          this.dismissedAt.set(this.content());
          return;
      }
    }

    if (event.key === 'Escape' && this.replyingTo()) {
      this.claim(event);
      this.onCancelReply();
    }
  }

  private claim(event: KeyboardEvent): void {
    event.preventDefault();
    event.stopPropagation();
  }

  /** Focus the textarea with the caret at the end, after the value has rendered. */
  private focusInput(): void {
    afterNextRender(
      () => {
        const el = this.promptInput()?.textareaRef()?.nativeElement;
        if (!el) return;
        el.focus();
        el.setSelectionRange(el.value.length, el.value.length);
      },
      { injector: this.injector },
    );
  }
}
