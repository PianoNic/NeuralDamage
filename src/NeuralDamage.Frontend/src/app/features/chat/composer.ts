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
  lucidePlus,
  lucideReply,
  lucideSmile,
  lucideX,
} from '@ng-icons/lucide';
import { HlmAvatarImports } from '@spartan-ng/helm/avatar';
import { HlmBadge } from '@spartan-ng/helm/badge';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmPopoverImports } from '@spartan-ng/helm/popover';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmSpinner } from '@spartan-ng/helm/spinner';
import { HlmTooltipImports } from '@spartan-ng/helm/tooltip';
import { firstValueFrom } from 'rxjs';
import { AttachmentsService } from '../../api/api/attachments.service';
import { describeApiError } from '../../core/http-errors';
import { ChatMember, Message } from '../../core/models';
import { initials } from '../../shared/initials';
import { IMAGE_TYPES, imageProblem, MAX_IMAGES_PER_MESSAGE } from './attachments';

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
  imports: [
    NgIcon,
    HlmAvatarImports,
    HlmBadge,
    HlmButton,
    HlmPopoverImports,
    HlmSpinner,
    HlmTooltipImports,
  ],
  providers: [
    provideIcons({
      lucideArrowUp,
      lucideAtSign,
      lucideImagePlus,
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
      [class.border-ring]="dragging()"
      [class.bg-accent]="dragging()"
      (click)="focusFromChrome($event)"
      (dragenter)="onDragOver($event)"
      (dragover)="onDragOver($event)"
      (dragleave)="onDragLeave($event)"
      (drop)="onDrop($event)"
    >
      @if (images().length) {
        <ul class="flex flex-wrap gap-2 pt-1" aria-label="Attached images">
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

      @if (replyingTo(); as reply) {
        <div class="bg-muted flex items-center gap-2 rounded-lg py-1 ps-2 pe-1 text-xs">
          <ng-icon name="lucideReply" class="text-muted-foreground shrink-0" />
          <span class="min-w-0 flex-1 truncate">
            <span class="text-muted-foreground">Replying to </span>
            <span class="font-medium">{{ reply.senderName }}</span>
            <span class="text-muted-foreground">: {{ reply.content || 'Image' }}</span>
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
        (paste)="onPaste($event)"
      ></textarea>

      <div class="flex items-center gap-0.5">
        <input
          #fileInput
          type="file"
          class="hidden"
          multiple
          [accept]="imageTypes"
          (change)="onFilesPicked($event)"
        />
        <button
          hlmBtn
          variant="ghost"
          size="icon-sm"
          type="button"
          aria-label="Attach images"
          hlmTooltip="Attach images"
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
  private readonly attachmentsApi = inject(AttachmentsService);
  private readonly textarea = viewChild.required<ElementRef<HTMLTextAreaElement>>('textarea');

  readonly members = input.required<ChatMember[]>();
  readonly replyingTo = input<Message | null>(null);
  readonly currentUserId = input<string | null>(null);
  readonly placeholder = input('Message');
  /** Where images are uploaded; without it, nothing can be attached. */
  readonly chatId = input<string | null>(null);

  readonly send = output<OutgoingMessage>();
  readonly typing = output<void>();
  readonly cancelReply = output<void>();

  private static nextId = 0;
  protected readonly textareaId = `composer-${Composer.nextId++}`;
  protected readonly listId = `${this.textareaId}-suggestions`;
  protected readonly counterId = `${this.textareaId}-counter`;
  protected readonly maxLength = MAX_MESSAGE_LENGTH;
  protected readonly emojis = QUICK_EMOJIS;
  protected readonly imageTypes = IMAGE_TYPES.join(',');

  readonly content = signal('');
  readonly images = signal<PendingImage[]>([]);
  protected readonly dragging = signal(false);
  private nextImageKey = 0;

  protected readonly tooLong = computed(() => this.content().length > MAX_MESSAGE_LENGTH);
  protected readonly showCounter = computed(() => this.content().length > COUNTER_FROM);
  private readonly uploading = computed(() => this.images().some((i) => !i.id));
  readonly canSend = computed(
    () =>
      (!!this.content().trim() || this.images().length > 0) && !this.tooLong() && !this.uploading(),
  );

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
    });
  }

  onValueChange(value: string): void {
    this.content.set(value);
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

    const room = MAX_IMAGES_PER_MESSAGE - this.images().length;
    if (files.length > room) {
      toast.error(`A message can carry at most ${MAX_IMAGES_PER_MESSAGE} images.`);
    }
    for (const file of files.slice(0, Math.max(room, 0))) {
      const problem = imageProblem(file);
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
      toast.error(describeApiError(error, { fallback: 'Could not upload the image.' }));
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
    this.content.set(suggestion.result);
    this.focusInput();
  }

  focus(): void {
    this.focusInput();
  }

  /** Appends `@name ` to the draft (the People panel's Mention) and puts the cursor after it. */
  insertMention(name: string): void {
    const text = this.content();
    const mention = `@${name} `;
    this.content.set(text && !/\s$/.test(text) ? `${text} ${mention}` : `${text}${mention}`);
    this.dismissedAt.set(this.content());
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
