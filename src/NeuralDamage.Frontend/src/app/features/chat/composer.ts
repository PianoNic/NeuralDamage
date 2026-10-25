import {
  afterNextRender,
  afterRenderEffect,
  Component,
  computed,
  DestroyRef,
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
  lucideImagePlus,
  lucideSlash,
  lucideReply,
  lucideSmile,
  lucideSticker,
  lucideX,
} from '@ng-icons/lucide';
import { MemberAvatar } from '../../shared/member-avatar';
import { HlmBadge } from '@spartan-ng/helm/badge';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmPopoverImports } from '@spartan-ng/helm/popover';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmSpinner } from '@spartan-ng/helm/spinner';
import { HlmTooltipImports } from '@spartan-ng/helm/tooltip';
import { firstValueFrom } from 'rxjs';
import { AttachmentsService } from '../../api/api/attachments.service';
import { toastApiError } from '../../core/http-errors';
import { ChatMember, Message } from '../../core/models';
import { AppConfig } from '../../core/app-config';
import { imageProblem } from './attachments';
import { findMentions, mentionAround } from './mentions';
import { StickerPicker } from '../stickers/sticker-picker';
import { convertShortcodes, EmojiIndex, loadEmojis, rememberEmoji, searchEmojis, trailingShortcode } from './emoji';
import { EmojiPicker } from './emoji-picker';
import { StickerDto } from '../../core/models';

/** The server's limit on a message body (SendMessageValidator). */
export const MAX_MESSAGE_LENGTH = 4000;

/** The counter shows from here, so the limit is no surprise. */
const COUNTER_FROM = MAX_MESSAGE_LENGTH - 500;

/**
 * A trailing "@query" still being typed. The query may hold spaces, for names like "Maggus Rühl",
 * so it runs to the end of the line; what it matches decides whether the list stays open.
 */
const TRAILING_MENTION = /(^|\s)@([^@\n]*)$/u;

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
  { command: '/compact', description: "Fold older messages into the bots' memory", arg: null },
  { command: '/kick', description: 'Remove a bot from this chat', arg: '<bot>' },
  { command: '/rename', description: 'Rename this chat', arg: '<name>' },
  { command: '/bots', description: 'List the bots in this chat', arg: null },
  { command: '/summarize', description: 'Get a recap of the chat, just for you', arg: null },
  { command: '/remember', description: 'Have a bot remember something for good, in this chat', arg: '<bot> <what>' },
  { command: '/forget', description: 'Have a bot forget something it remembers', arg: '<bot> <what>' },
  { command: '/help', description: 'Show available commands', arg: null },
] as const;

/** How many emoji a `:query` offers. */
const MAX_EMOJI_SUGGESTIONS = 8;

/** An image picked for the next message: previewed locally, uploaded straight away. */
export interface PendingImage {
  key: number;
  name: string;
  previewUrl: string;
  /** The server's id once the upload is done. */
  id: string | null;
}

/** What the composer sends. */
export interface OutgoingMessage {
  content: string;
  mentions: string[];
  attachmentIds: string[];
}

export interface Suggestion {
  key: string;
  kind: 'command' | 'member' | 'emoji';
  label: string;
  emoji?: string;
  description?: string;
  member?: ChatMember;
  /** The whole composer text once this suggestion is picked. */
  result: string;
}

/**
 * The message box: text on top, then attach / mention / emoji and a round send button. Slash
 * commands, @mentions and :shortcodes: complete in a Command-style popover above it, driven from the
 * keyboard; a complete `:code:` turns into its emoji as it is typed or pasted.
 */
@Component({
  selector: 'app-composer',
  imports: [
    NgIcon,
    MemberAvatar,
    HlmBadge,
    HlmButton,
    HlmPopoverImports,
    HlmSpinner,
    HlmTooltipImports,
    StickerPicker,
    EmojiPicker,
  ],
  providers: [
    provideIcons({
      lucideArrowUp,
      lucideAtSign,
      lucideImagePlus,
      lucideSlash,
      lucideReply,
      lucideSmile,
      lucideSticker,
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
              } @else if (item.kind === 'emoji') {
                <span class="w-6 text-center text-lg leading-none">{{ item.emoji }}</span>
                <span class="truncate">:{{ item.label }}:</span>
              } @else {
                <app-member-avatar [name]="item.label" [avatarUrl]="item.member?.avatarUrl" [modelId]="item.member?.modelId" [px]="24" />
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
      class="border-input bg-card focus-within:border-ring flex cursor-text flex-col gap-1 rounded-2xl border px-3 pt-2.5 pb-2 shadow-xs transition-colors"
      [class]="pill() ? 'max-md:min-h-12 max-md:flex-row max-md:flex-wrap max-md:items-center max-md:gap-x-0.5 max-md:rounded-3xl max-md:px-1.5 max-md:py-1' : ''"
      [class.border-ring]="dragging()"
      [class.bg-accent]="dragging()"
      (click)="focusFromChrome($event)"
      (dragenter)="onDragOver($event)"
      (dragover)="onDragOver($event)"
      (dragleave)="onDragLeave($event)"
      (drop)="onDrop($event)"
    >
      @if (images().length) {
        <ul class="flex min-w-0 flex-wrap gap-2 pt-1 max-md:basis-full max-md:px-1.5" aria-label="Attached images">
          @for (image of images(); track image.key) {
            <li class="relative size-16 shrink-0">
              <img
                [src]="image.previewUrl"
                [alt]="image.name"
                class="bg-muted size-full rounded-lg border object-cover"
                [class.opacity-50]="!image.id"
              />
              @if (!image.id) {
                <span class="absolute inset-0 flex items-center justify-center" aria-label="Uploading">
                  <hlm-spinner />
                </span>
              }
              <button
                hlmBtn
                variant="secondary"
                size="icon-xs"
                type="button"
                class="absolute -end-1.5 -top-1.5 rounded-full border shadow-xs"
                [attr.aria-label]="'Remove ' + image.name"
                (click)="removeImage(image.key)"
              >
                <ng-icon name="lucideX" />
              </button>
            </li>
          }
        </ul>
      }

      <!-- Something else going out with the text, such as a generated picture on the feed. -->
      <ng-content select="[composerAttachment]" />

      @if (replyingTo(); as reply) {
        <div class="bg-muted flex min-w-0 items-center gap-2 rounded-lg py-1 ps-2 pe-1 text-xs max-md:my-0.5 max-md:basis-full max-md:rounded-2xl">
          <ng-icon name="lucideReply" class="text-muted-foreground shrink-0" />
          <span class="min-w-0 flex-1 truncate">
            <span class="text-muted-foreground">Replying to </span>
            <span class="font-medium">{{ reply.senderName }}</span>
            <span class="text-muted-foreground">: {{ reply.content || (reply.sticker ? 'Sticker' : 'Image') }}</span>
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
      <!--
        Mentions are chips: the textarea keeps the plain "@Name" text (so typing, IME, undo, paste and
        mobile keyboards stay native), and this copy of it behind, laid out identically, tints them.
      -->
      <div class="relative" [class]="pill() ? 'max-md:order-2 max-md:min-w-0 max-md:flex-1 max-md:px-1.5' : ''">
        <div
          #mirror
          aria-hidden="true"
          data-testid="composer-mirror"
          class="pointer-events-none absolute inset-0 overflow-hidden px-1 py-1 text-sm leading-normal wrap-break-word whitespace-pre-wrap text-transparent"
        >@for (part of parts(); track $index) {<span [class]="part.mention ? chipClass : ''" [attr.data-mention]="part.mention || null">{{ part.text }}</span>}</div>
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
        class="placeholder:text-muted-foreground relative block max-h-50 min-h-7 w-full resize-none bg-transparent px-1 py-1 text-sm leading-normal outline-none [scrollbar-width:none] [&::-webkit-scrollbar]:hidden"
        (input)="onInput($event)"
        (keydown)="onKeyDown($event)"
        (paste)="onPaste($event)"
        (scroll)="syncMirror()"
        (pointerdown)="pointing = true"
        (pointerup)="pointing = false; snapSelection(true)"
        (keyup)="snapSelection()"
        (compositionstart)="composing = true"
        (compositionend)="composing = false"
      ></textarea>
      </div>

      <div class="flex items-center gap-0.5" [class.max-md:contents]="pill()">
        <input
          #fileInput
          type="file"
          class="hidden"
          multiple
          [accept]="imageTypes()"
          (change)="onFilesPicked($event)"
        />
        @if (!feed()) {
          <button
            hlmBtn
            variant="ghost"
            size="icon-sm"
            type="button"
            aria-label="Attach images"
            hlmTooltip="Attach images"
            [class]="pill() ? 'max-md:bg-primary max-md:text-primary-foreground max-md:hover:bg-primary/90 max-md:order-1 max-md:size-9 max-md:rounded-full' : ''"
            [disabled]="!chatId()"
            (click)="fileInput.click()"
          >
            <ng-icon name="lucideImagePlus" />
          </button>
          <button
            hlmBtn
            variant="ghost"
            size="icon-sm"
            type="button"
            aria-label="Commands"
            hlmTooltip="Commands"
            [class]="pill() ? (drafting() ? 'max-md:hidden' : 'max-md:order-3') : ''"
            (click)="insertTrigger('/')"
          >
            <ng-icon name="lucideSlash" />
          </button>
        }
        <button
          hlmBtn
          variant="ghost"
          size="icon-sm"
          type="button"
          aria-label="Mention someone"
          hlmTooltip="Mention"
          [class.max-md:hidden]="pill()"
          (click)="insertTrigger('@')"
        >
          <ng-icon name="lucideAtSign" />
        </button>
        <hlm-popover align="start" [sideOffset]="8" [class.max-md:order-3]="pill()">
          <button
            hlmPopoverTrigger
            hlmBtn
            variant="ghost"
            size="icon-sm"
            type="button"
            aria-label="Insert emoji"
            hlmTooltip="Emoji"
          >
            <ng-icon name="lucideSmile" />
          </button>
          <hlm-popover-content *hlmPopoverPortal="let ctx" class="w-auto p-2">
            <!-- The dataset is a lazy chunk; the picker loads it on first open. -->
            <app-emoji-picker (pick)="insertEmoji($event); ctx.close()" />
          </hlm-popover-content>
        </hlm-popover>
        <hlm-popover align="start" [sideOffset]="8" [class]="pill() ? (drafting() ? 'max-md:hidden' : 'max-md:order-3') : ''">
          <button
            hlmPopoverTrigger
            hlmBtn
            variant="ghost"
            size="icon-sm"
            type="button"
            aria-label="Send a sticker"
            hlmTooltip="Stickers"
            [disabled]="!chatId() && !feed()"
          >
            <ng-icon name="lucideSticker" />
          </button>
          <hlm-popover-content *hlmPopoverPortal="let ctx" class="w-auto p-2">
            <app-sticker-picker (pick)="sendSticker.emit($event); ctx.close()" />
          </hlm-popover-content>
        </hlm-popover>
        <ng-content select="[composerTool]" />

        <span class="flex-1" [class.max-md:hidden]="pill()"></span>

        @if (showCounter()) {
          <span
            [id]="counterId"
            class="me-2 text-xs tabular-nums max-md:order-3"
            [class.text-destructive]="tooLong()"
            [class.text-muted-foreground]="!tooLong()"
          >
            {{ content().length }} / {{ limit() }}
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
          [class]="pill() ? (drafting() ? 'max-md:order-4 max-md:size-9' : 'max-md:hidden') : ''"
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
  private readonly attachmentsApi = inject(AttachmentsService);
  private readonly textarea = viewChild.required<ElementRef<HTMLTextAreaElement>>('textarea');
  private readonly mirror = viewChild.required<ElementRef<HTMLElement>>('mirror');

  readonly members = input.required<ChatMember[]>();
  /** Every name that mentions someone (display names and bots' nicknames); defaults to the display names. */
  readonly mentionNames = input<readonly string[] | null>(null);
  readonly replyingTo = input<Message | null>(null);
  readonly currentUserId = input<string | null>(null);
  readonly placeholder = input('Message');
  /** Where images are uploaded; without it, nothing can be attached. */
  readonly chatId = input<string | null>(null);

  readonly send = output<OutgoingMessage>();
  /** A sticker picked in the picker; it goes out at once, as a message of its own. */
  readonly sendSticker = output<StickerDto>();
  readonly typing = output<void>();
  readonly cancelReply = output<void>();

  private static nextId = 0;
  protected readonly textareaId = `composer-${Composer.nextId++}`;
  protected readonly listId = `${this.textareaId}-suggestions`;
  protected readonly counterId = `${this.textareaId}-counter`;
  /**
   * Outside a chat, on the feed: no images or slash commands, and stickers work without a chat.
   * @mentions and emoji work the same.
   */
  readonly feed = input(false);
  /** The longest text it sends. */
  readonly limit = input(MAX_MESSAGE_LENGTH);
  /**
   * A projected attachment (`composerAttachment`): `busy` holds sending back until it is done,
   * `ready` lets it go out without any text.
   */
  readonly attachment = input<'none' | 'busy' | 'ready'>('none');
  /**
   * On phones, one rounded bar like Instagram's: images on the left, the text, then the tools, which
   * make way for Send once there is something to send. Off, it keeps the text above the tools, for a
   * caption.
   */
  readonly pill = input(true);
  protected readonly maxLength = MAX_MESSAGE_LENGTH;
  private readonly limits = inject(AppConfig).uploadLimits;
  protected readonly imageTypes = computed(() => this.limits().contentTypes.join(','));

  readonly content = signal('');
  readonly images = signal<PendingImage[]>([]);
  protected readonly dragging = signal(false);
  private nextImageKey = 0;

  /** Tints a mention in the copy behind the textarea; the padding is taken back so nothing moves. */
  protected readonly chipClass = 'bg-primary/14 -mx-[0.15em] rounded-[0.3em] px-[0.15em] box-decoration-clone';

  private readonly names = computed(() => this.mentionNames() ?? this.members().map((m) => m.displayName));
  /** The chips: every "@Name" of a member, exactly as the server will read it. */
  readonly mentions = computed(() => findMentions(this.content(), this.names()));
  /** The text cut at the chips, for the copy behind the textarea. The last part keeps a final newline's line. */
  protected readonly parts = computed(() => {
    const text = this.content();
    const parts: { text: string; mention: boolean }[] = [];
    let at = 0;
    for (const m of this.mentions()) {
      if (m.start > at) parts.push({ text: text.slice(at, m.start), mention: false });
      parts.push({ text: text.slice(m.start, m.end), mention: true });
      at = m.end;
    }
    parts.push({ text: text.slice(at) + '\u200b', mention: false });
    return parts;
  });

  /** Set while a pointer is selecting, or an IME is composing: the caret is theirs until they finish. */
  protected pointing = false;
  protected composing = false;
  private lastSelection = { start: 0, end: 0 };

  protected readonly tooLong = computed(() => this.content().length > this.limit());
  protected readonly showCounter = computed(
    () => this.content().length > (this.limit() === MAX_MESSAGE_LENGTH ? COUNTER_FROM : this.limit() * 0.8),
  );
  private readonly uploading = computed(() => this.images().some((i) => !i.id));
  /** Something typed or attached: the phone bar swaps its tools for Send. */
  protected readonly drafting = computed(() => !!this.content() || this.images().length > 0);
  readonly canSend = computed(
    () =>
      (!!this.content().trim() || this.images().length > 0 || this.attachment() === 'ready') &&
      !this.tooLong() &&
      !this.uploading() &&
      this.attachment() !== 'busy',
  );

  /** The emoji dataset, once its lazy chunk has loaded (on the first `:`). */
  readonly emojiIndex = signal<EmojiIndex | null>(null);

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

    const partial = this.feed() ? null : PARTIAL_COMMAND.exec(text);
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

    // ":sob" at a word start; checked before mentions, whose query may run on past a space.
    const shortcode = trailingShortcode(text, this.mentions());
    if (shortcode) {
      const index = this.emojiIndex();
      if (!index) return [];
      const before = text.slice(0, shortcode.start);
      return searchEmojis(index, shortcode.query, MAX_EMOJI_SUGGESTIONS).map((e) => ({
        key: e.emoji,
        kind: 'emoji' as const,
        label: e.shortcodes.find((c) => c.startsWith(shortcode.query)) ?? e.shortcodes[0],
        emoji: e.emoji,
        result: before + e.emoji,
      }));
    }

    const mention = TRAILING_MENTION.exec(text);
    if (mention) {
      const query = mention[2].toLowerCase();
      // A query with a space only goes on matching a name that starts with it ("@Maggus R").
      const matches = /\s/.test(query)
        ? (name: string) => name.startsWith(query)
        : (name: string) => name.includes(query);
      const before = text.slice(0, mention.index + mention[1].length);
      return this.others()
        .filter((m) => m.displayName && matches(m.displayName.toLowerCase()))
        .map((m) => ({
          key: m.id,
          kind: 'member' as const,
          label: m.displayName,
          member: m,
          result: `${before}@${m.displayName} `,
        }));
    }

    return [];
  });

  protected readonly suggestionLabel = computed(
    () => ({ command: 'Commands', member: 'Members', emoji: 'Emoji' })[this.suggestions()[0]?.kind ?? 'member'],
  );

  /** Keyboard selection; back to the top whenever the list changes. */
  readonly activeIndex = linkedSignal({ source: this.suggestions, computation: () => 0 });

  constructor() {
    // Images belong to the chat they were uploaded to; switching chats drops them.
    effect(() => {
      this.chatId();
      untracked(() => this.clearImages());
    });
    inject(DestroyRef).onDestroy(() => this.clearImages());

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
      this.syncMirror();
    });

    // The caret never rests inside a chip: arrows, Home/End and taps all land on its edges.
    const onSelectionChange = () => this.snapSelection();
    document.addEventListener('selectionchange', onSelectionChange);
    inject(DestroyRef).onDestroy(() => document.removeEventListener('selectionchange', onSelectionChange));
  }

  protected onInput(event: Event): void {
    const el = event.target as HTMLTextAreaElement;
    const inputType = (event as InputEvent).inputType ?? '';
    // A deletion that took only part of a chip (a phone keyboard's backspace skips keydown) takes it all.
    if (inputType.startsWith('delete')) {
      const whole = wholeMentionDeletion(this.content(), el.value, this.names());
      if (whole) {
        el.value = whole.value;
        el.setSelectionRange(whole.caret, whole.caret);
      }
    }
    // A ":code:" just typed or pasted becomes its emoji; IME text is left until it is committed.
    const composing = (event as InputEvent).isComposing || inputType === 'insertCompositionText';
    if (inputType.startsWith('insert') && !composing && this.convertInserted(this.content(), el.value)) return;
    this.onValueChange(el.value);
  }

  /**
   * Converts the known `:code:`s that end in the part of `after` that differs from `before`, as one
   * undoable edit, keeping the caret where it was relative to the text after them.
   */
  private convertInserted(before: string, after: string): boolean {
    if (!after.includes(':')) return false;
    const index = this.emojiIndex();
    if (!index) {
      // The first ":code:" may be pasted before the dataset is here: convert once it is.
      void this.loadEmojiIndex().then(() => {
        if (this.textarea().nativeElement.value === after) this.convertInserted(before, after);
      });
      return false;
    }
    let from = 0;
    while (from < before.length && from < after.length && before[from] === after[from]) from++;
    let tail = 0;
    while (tail < before.length - from && tail < after.length - from && before.at(-1 - tail) === after.at(-1 - tail)) tail++;
    const to = after.length - tail;
    const hit = convertShortcodes(after, index, from, to, findMentions(after, this.names()));
    if (!hit) return false;
    const caret = to - (hit.end - hit.start) + hit.text.length;
    this.replaceRange(hit.start, hit.end, hit.text, caret);
    return true;
  }

  private loadEmojiIndex(): Promise<EmojiIndex> {
    return loadEmojis().then((index) => {
      this.emojiIndex.set(index);
      return index;
    });
  }

  /** Keeps the copy behind the textarea scrolled with it. */
  protected syncMirror(): void {
    this.mirror().nativeElement.scrollTop = this.textarea().nativeElement.scrollTop;
  }

  /**
   * Moves a caret or selection edge that fell inside a chip to the chip's edge: on in the direction
   * it was moving, so arrows and word jumps skip the chip, or the nearer edge for a click or tap.
   */
  snapSelection(nearest = false): void {
    const el = this.textarea().nativeElement;
    const { selectionStart: start, selectionEnd: end } = el;
    if (this.pointing || this.composing || document.activeElement !== el) {
      this.lastSelection = { start, end };
      return;
    }
    const mentions = findMentions(el.value, this.names());
    const snap = (at: number, from: number) => {
      const m = mentionAround(mentions, at);
      if (!m) return at;
      if (!nearest && from <= m.start) return m.end;
      if (!nearest && from >= m.end) return m.start;
      return at - m.start < m.end - at ? m.start : m.end;
    };
    const newStart = snap(start, this.lastSelection.start);
    const newEnd = start === end ? newStart : snap(end, this.lastSelection.end);
    this.lastSelection = { start: newStart, end: newEnd };
    if (newStart !== start || newEnd !== end) el.setSelectionRange(newStart, newEnd, el.selectionDirection);
  }

  onValueChange(value: string): void {
    this.content.set(value);
    if (!this.emojiIndex() && value.includes(':')) void this.loadEmojiIndex();
    if (value.trim()) this.typing.emit();
  }

  onSend(): void {
    const text = this.content().trim();
    if (!this.canSend()) return;
    const attachmentIds = this.images().flatMap((i) => (i.id ? [i.id] : []));
    this.send.emit({ content: text, mentions: [], attachmentIds });
    this.content.set('');
    this.clearImages();
    // If Enter lands before the last keystroke has rendered, the [value] binding sees '' before
    // and after and leaves the sent text in the box, so clear the box directly.
    this.textarea().nativeElement.value = '';
  }

  /** Validates and uploads images, up to the per-message cap, showing each as it goes. */
  addImages(files: readonly File[]): void {
    const chatId = this.chatId();
    if (!chatId || files.length === 0) return;

    const limits = this.limits();
    const room = limits.maxPerMessage - this.images().length;
    if (files.length > room) {
      toast.error(`A message can carry at most ${limits.maxPerMessage} images.`, {
        id: 'too-many-images',
      });
    }
    for (const file of files.slice(0, Math.max(room, 0))) {
      const problem = imageProblem(file, limits);
      if (problem) {
        toast.error(problem);
        continue;
      }
      void this.upload(chatId, file);
    }
    this.focusInput();
  }

  removeImage(key: number): void {
    const image = this.images().find((i) => i.key === key);
    if (image) revokePreview(image.previewUrl);
    this.images.update((list) => list.filter((i) => i.key !== key));
  }

  protected onFilesPicked(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.addImages(Array.from(input.files ?? []));
    // Picking the same file again must fire change again.
    input.value = '';
  }

  protected onPaste(event: ClipboardEvent): void {
    const files = Array.from(event.clipboardData?.files ?? []);
    if (files.length === 0) return;
    // A pasted screenshot would otherwise also paste its file name as text.
    event.preventDefault();
    this.addImages(files);
  }

  protected onDragOver(event: DragEvent): void {
    if (!event.dataTransfer?.types.includes('Files')) return;
    event.preventDefault();
    event.dataTransfer.dropEffect = this.chatId() ? 'copy' : 'none';
    this.dragging.set(true);
  }

  protected onDragLeave(event: DragEvent): void {
    // Leaving for a child is not leaving the box.
    const to = event.relatedTarget as Node | null;
    if (to && (event.currentTarget as HTMLElement).contains(to)) return;
    this.dragging.set(false);
  }

  protected onDrop(event: DragEvent): void {
    if (!event.dataTransfer?.types.includes('Files')) return;
    event.preventDefault();
    this.dragging.set(false);
    this.addImages(Array.from(event.dataTransfer.files));
  }

  private async upload(chatId: string, file: File): Promise<void> {
    const key = this.nextImageKey++;
    const previewUrl = createPreview(file);
    this.images.update((list) => [
      ...list,
      { key, name: file.name || 'Pasted image', previewUrl, id: null },
    ]);
    try {
      const uploaded = await firstValueFrom(
        this.attachmentsApi.apiChatsChatIdAttachmentsPost(chatId, file),
      );
      this.images.update((list) => list.map((i) => (i.key === key ? { ...i, id: uploaded.id } : i)));
    } catch (error) {
      this.removeImage(key);
      toastApiError(error, { fallback: 'Could not upload the image.' });
    }
  }

  private clearImages(): void {
    for (const image of this.images()) revokePreview(image.previewUrl);
    this.images.set([]);
  }

  /** Puts back text whose send failed, unless something new has been typed since. */
  restoreDraft(text: string): void {
    if (!this.content().trim()) this.content.set(text);
  }

  pick(suggestion: Suggestion): void {
    this.replaceAll(suggestion.result);
    // A picked name stays picked: "@Rex " must not reopen the list for "Rex Jr".
    if (suggestion.kind === 'member') this.dismissedAt.set(suggestion.result);
    if (suggestion.emoji) rememberEmoji(suggestion.emoji);
  }

  focus(): void {
    this.focusInput();
  }

  /** Appends `@name ` to the draft (the People panel's Mention) and puts the cursor after it. */
  insertMention(name: string): void {
    const text = this.content();
    const mention = `@${name} `;
    const next = text && !/\s$/.test(text) ? `${text} ${mention}` : `${text}${mention}`;
    this.replaceAll(next);
    this.dismissedAt.set(next);
  }

  protected insertTrigger(trigger: '/' | '@'): void {
    const text = this.content();
    if (trigger === '/') this.replaceAll(text.startsWith('/') ? text : '/');
    else this.replaceAll(text && !/\s$/.test(text) ? `${text} @` : `${text}@`);
    this.dismissedAt.set(null);
  }

  insertEmoji(emoji: string): void {
    const el = this.textarea().nativeElement;
    const at = el.selectionStart ?? el.value.length;
    this.replaceRange(at, el.selectionEnd ?? at, emoji);
  }

  /** Turns the text into `next`, editing only from where it differs, and puts the caret at the end. */
  private replaceAll(next: string): void {
    const text = this.textarea().nativeElement.value;
    let start = 0;
    while (start < text.length && start < next.length && text[start] === next[start]) start++;
    this.replaceRange(start, text.length, next.slice(start));
  }

  /**
   * Replaces `start`..`end` with `text` as typing would, so Ctrl+Z takes it back, and leaves the caret
   * after it (or at `caret`). Where the browser cannot (no execCommand), it sets the value instead.
   */
  private replaceRange(start: number, end: number, text: string, caret = start + text.length): void {
    const el = this.textarea().nativeElement;
    const expected = el.value.slice(0, start) + text + el.value.slice(end);
    el.focus();
    el.setSelectionRange(start, end);
    let typed = false;
    try {
      typed = !!text && typeof document.execCommand === 'function' && document.execCommand('insertText', false, text);
    } catch {
      typed = false;
    }
    if (!typed || el.value !== expected) {
      el.value = expected;
      this.onValueChange(expected);
    }
    el.setSelectionRange(caret, caret);
    this.lastSelection = { start: caret, end: caret };
    this.focusInput(caret);
  }

  protected onKeyDown(event: KeyboardEvent): void {
    if (!event.isComposing && this.stepOverChip(event)) return;

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

  /**
   * Backspace and Delete next to a chip select all of it, so the browser's own (undoable) deletion
   * takes it whole, and a selection that cuts into a chip grows to cover it. Left and Right step over
   * a chip in one press. Returns whether the key was handled here.
   */
  private stepOverChip(event: KeyboardEvent): boolean {
    const el = this.textarea().nativeElement;
    const { selectionStart: start, selectionEnd: end } = el;
    const mentions = findMentions(el.value, this.names());
    if (!mentions.length) return false;

    if (event.key === 'Backspace' || event.key === 'Delete') {
      let from = start;
      let to = end;
      if (start === end) {
        const back = event.key === 'Backspace';
        const chip = mentions.find((m) => (back ? m.end : m.start) === start) ?? mentionAround(mentions, start);
        if (!chip) return false;
        [from, to] = [chip.start, chip.end];
      } else {
        from = mentionAround(mentions, start)?.start ?? start;
        to = mentionAround(mentions, end)?.end ?? end;
      }
      if (from !== start || to !== end) el.setSelectionRange(from, to);
      return false;
    }

    const plain = !event.shiftKey && !event.altKey && !event.ctrlKey && !event.metaKey;
    if (plain && start === end && (event.key === 'ArrowLeft' || event.key === 'ArrowRight')) {
      const left = event.key === 'ArrowLeft';
      const chip = mentions.find((m) => (left ? m.end : m.start) === start);
      if (!chip) return false;
      event.preventDefault();
      const at = left ? chip.start : chip.end;
      el.setSelectionRange(at, at);
      this.lastSelection = { start: at, end: at };
      return true;
    }
    return false;
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

/**
 * After a deletion turned `before` into `after`: if it took part of a chip, the text with the whole
 * chip gone and where the caret goes; null when every chip was left whole or removed whole.
 */
export function wholeMentionDeletion(
  before: string,
  after: string,
  names: readonly string[],
): { value: string; caret: number } | null {
  if (after.length >= before.length) return null;
  let start = 0;
  while (start < after.length && before[start] === after[start]) start++;
  let tail = 0;
  while (tail < after.length - start && before[before.length - 1 - tail] === after[after.length - 1 - tail]) tail++;
  let from = start;
  let to = before.length - tail;
  const cut = findMentions(before, names).filter((m) => m.start < to && m.end > from);
  if (!cut.some((m) => m.start < from || m.end > to)) return null;
  for (const m of cut) {
    from = Math.min(from, m.start);
    to = Math.max(to, m.end);
  }
  return { value: before.slice(0, from) + before.slice(to), caret: from };
}

function createPreview(file: File): string {
  try {
    return URL.createObjectURL(file);
  } catch {
    return '';
  }
}

function revokePreview(url: string): void {
  if (url && typeof URL.revokeObjectURL === 'function') URL.revokeObjectURL(url);
}
