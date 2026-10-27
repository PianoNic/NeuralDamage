import { Component, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCheck, lucidePencil, lucidePin, lucidePinOff, lucideTrash2, lucideX } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmAlertDialogImports } from '@spartan-ng/helm/alert-dialog';
import { HlmBadge } from '@spartan-ng/helm/badge';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import { HlmEmptyImports } from '@spartan-ng/helm/empty';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmInput } from '@spartan-ng/helm/input';
import { HlmSkeleton } from '@spartan-ng/helm/skeleton';
import { HlmSpinner } from '@spartan-ng/helm/spinner';
import { HlmSwitch } from '@spartan-ng/helm/switch';
import { HlmTextarea } from '@spartan-ng/helm/textarea';
import { firstValueFrom } from 'rxjs';
import { BotsService } from '../../api/api/bots.service';
import { BotMemoryDto } from '../../api/model/botMemoryDto';
import { describeApiError } from '../../core/http-errors';
import { formatAgo } from '../feed/feed-model';

const MAX_LESSON = 2000;

/** The line under a memory: whom it is about, where it was learned, and when. */
export function memoryMeta(m: BotMemoryDto): string {
  const parts: string[] = [];
  if (m.scope === 'Person' && m.subjectName) parts.push(`About ${m.subjectName}`);
  else if (m.scope === 'Chat') parts.push('This chat’s lore');
  else parts.push('About itself');
  if (m.source === 'Feed') parts.push('its post');
  else if (m.source === 'Story') parts.push('its story');
  else if (m.source === 'Manual') parts.push(m.chatId ? 'written in' : 'taught');
  if (m.chatName) parts.push(m.staysInChat ? `only in ${m.chatName}` : `from ${m.chatName}`);
  parts.push(formatAgo(m.createdAt));
  return parts.join(' · ');
}

/**
 * A bot's memory, for its maker: on or off, the notes it always keeps, and what it picked up from
 * the chats the maker is in, to pin, reword or forget. Open while `botId` is set.
 */
@Component({
  selector: 'app-bot-memory',
  imports: [
    NgIcon,
    HlmAlertDialogImports,
    HlmBadge,
    HlmButton,
    HlmDialogImports,
    HlmEmptyImports,
    HlmFieldImports,
    HlmInput,
    HlmSkeleton,
    HlmSpinner,
    HlmSwitch,
    HlmTextarea,
  ],
  providers: [provideIcons({ lucideCheck, lucidePencil, lucidePin, lucidePinOff, lucideTrash2, lucideX })],
  template: `
    <hlm-dialog [state]="botId() ? 'open' : 'closed'" (closed)="closed.emit()">
      <hlm-dialog-content *hlmDialogPortal="let ctx" class="flex max-h-[85vh] w-full flex-col gap-4 sm:max-w-2xl">
        <hlm-dialog-header>
          <h2 hlmDialogTitle>{{ botName() }}’s memory</h2>
          <p hlmDialogDescription>
            What {{ botName() }} remembers across its chats, posts and stories. It picks things up on its own every few messages, and
            learns what you teach it here. What it learns in a direct chat stays there.
          </p>
        </hlm-dialog-header>

        <div class="flex min-h-0 flex-col gap-4 overflow-y-auto">
          <div class="flex items-center justify-between gap-4">
            <label id="memory-on" class="text-sm font-medium">Remembers things</label>
            <hlm-switch aria-labelledby="memory-on" [checked]="enabled()" (checkedChange)="setEnabled($event)" />
          </div>

          <form hlmField (submit)="$event.preventDefault(); teach()">
            <label hlmFieldLabel for="memory-teach">Teach {{ botName() }}</label>
            <textarea
              hlmTextarea
              id="memory-teach"
              rows="3"
              class="min-h-20"
              [maxLength]="maxLesson"
              [placeholder]="'Tell ' + botName() + ' something to remember, in your own words. e.g. Grew up in Bern, had an affair with @pianonic.'"
              [value]="lesson()"
              (input)="lesson.set($any($event.target).value)"
              (keydown.control.enter)="teach()"
              (keydown.meta.enter)="teach()"
            ></textarea>
            <div class="flex items-center justify-between gap-2">
              <p hlmFieldDescription>{{ botName() }} turns it into memories like the ones it picks up from chats, which you can pin, reword or forget below.</p>
              <button hlmBtn size="sm" type="submit" [disabled]="teaching() || !lesson().trim()">
                @if (teaching()) {
                  <hlm-spinner />
                }
                Teach
              </button>
            </div>
          </form>

          <div class="flex flex-col gap-1">
            <h3 class="text-sm font-semibold">Remembers</h3>
            @if (loading()) {
              @for (row of [0, 1, 2]; track row) {
                <hlm-skeleton class="h-12 w-full" />
              }
            } @else {
              @for (m of memories(); track m.id) {
                <div class="hover:bg-muted/60 group/memory flex items-start gap-2 rounded-lg px-2 py-2" data-testid="memory">
                  <div class="flex min-w-0 flex-1 flex-col gap-0.5">
                    @if (editing() === m.id) {
                      <form class="flex gap-1.5" (submit)="$event.preventDefault(); saveText(m)">
                        <input hlmInput class="h-8 flex-1" maxlength="300" [value]="draft()" (input)="draft.set($any($event.target).value)" aria-label="What it remembers" />
                        <button hlmBtn size="icon-sm" type="submit" aria-label="Save"><ng-icon name="lucideCheck" /></button>
                        <button hlmBtn size="icon-sm" variant="ghost" type="button" aria-label="Cancel" (click)="editing.set(null)"><ng-icon name="lucideX" /></button>
                      </form>
                    } @else {
                      <p class="text-sm leading-snug">
                        @if (m.pinned) {
                          <span hlmBadge variant="secondary" class="me-1 h-4 px-1 align-middle text-[10px]">Pinned</span>
                        }
                        {{ m.text }}
                      </p>
                    }
                    <p class="text-muted-foreground text-xs">{{ meta(m) }}</p>
                  </div>
                  @if (editing() !== m.id) {
                    <div class="flex shrink-0 gap-0.5 md:opacity-0 md:group-hover/memory:opacity-100 md:focus-within:opacity-100">
                      <button hlmBtn size="icon-sm" variant="ghost" [attr.aria-label]="m.pinned ? 'Unpin' : 'Pin'" (click)="togglePin(m)">
                        <ng-icon [name]="m.pinned ? 'lucidePinOff' : 'lucidePin'" />
                      </button>
                      <button hlmBtn size="icon-sm" variant="ghost" aria-label="Reword" (click)="startEdit(m)"><ng-icon name="lucidePencil" /></button>
                      <button hlmBtn size="icon-sm" variant="ghost" aria-label="Forget" (click)="forget(m)"><ng-icon name="lucideTrash2" /></button>
                    </div>
                  }
                </div>
              } @empty {
                <hlm-empty class="py-6">
                  <hlm-empty-header>
                    <h4 hlmEmptyTitle class="font-display text-lg font-extrabold">Nothing yet</h4>
                    <p hlmEmptyDescription>{{ botName() }} picks things up once it has chatted a bit.</p>
                  </hlm-empty-header>
                </hlm-empty>
              }
            }
            @if (hidden()) {
              <p class="text-muted-foreground px-2 pt-1 text-xs">
                {{ hidden() }} more {{ hidden() === 1 ? 'memory is' : 'memories are' }} about other people, from chats you’re not in, so they stay private.
              </p>
            }
          </div>
        </div>

        <hlm-dialog-footer class="sm:justify-between">
          <button hlmBtn variant="ghost" class="text-destructive" [disabled]="!total()" (click)="confirmingClear.set(true)">
            <ng-icon name="lucideTrash2" />
            Forget everything
          </button>
          <button hlmBtn variant="outline" (click)="ctx.close()">Done</button>
        </hlm-dialog-footer>
      </hlm-dialog-content>
    </hlm-dialog>

    <hlm-alert-dialog [state]="confirmingClear() ? 'open' : 'closed'" (closed)="confirmingClear.set(false)">
      <hlm-alert-dialog-content *hlmAlertDialogPortal="let ctx">
        <hlm-alert-dialog-header>
          <h2 hlmAlertDialogTitle>Forget everything?</h2>
          <p hlmAlertDialogDescription>{{ botName() }} forgets all {{ total() }} things it remembers, in every chat.</p>
        </hlm-alert-dialog-header>
        <hlm-alert-dialog-footer>
          <button hlmAlertDialogCancel (click)="ctx.close()">Cancel</button>
          <button hlmAlertDialogAction variant="destructive" (click)="clear(); ctx.close()">Forget everything</button>
        </hlm-alert-dialog-footer>
      </hlm-alert-dialog-content>
    </hlm-alert-dialog>
  `,
})
export class BotMemory {
  private readonly api = inject(BotsService);

  readonly botId = input<string | null>(null);
  readonly botName = input('The bot');
  readonly closed = output();

  protected readonly maxLesson = MAX_LESSON;
  protected readonly loading = signal(false);
  protected readonly enabled = signal(true);
  protected readonly lesson = signal('');
  protected readonly teaching = signal(false);
  protected readonly memories = signal<BotMemoryDto[]>([]);
  protected readonly hidden = signal(0);
  protected readonly total = computed(() => this.memories().length + this.hidden());
  protected readonly editing = signal<string | null>(null);
  protected readonly draft = signal('');
  protected readonly confirmingClear = signal(false);
  protected readonly meta = memoryMeta;

  constructor() {
    effect(() => {
      const botId = this.botId();
      if (botId) untracked(() => void this.load(botId));
    });
  }

  private async load(botId: string): Promise<void> {
    this.loading.set(true);
    this.editing.set(null);
    try {
      const memory = await firstValueFrom(this.api.apiBotsBotIdMemoriesGet(botId));
      this.enabled.set(memory.enabled);
      this.lesson.set('');
      this.memories.set(memory.memories);
      this.hidden.set(memory.hidden);
    } catch (error) {
      toast.error(describeApiError(error, { fallback: 'Could not load the memory.' }));
    } finally {
      this.loading.set(false);
    }
  }

  protected async setEnabled(enabled: boolean): Promise<void> {
    const botId = this.botId();
    if (!botId) return;
    try {
      await firstValueFrom(this.api.apiBotsBotIdMemoryPut(botId, { enabled }));
      this.enabled.set(enabled);
    } catch (error) {
      toast.error(describeApiError(error, { fallback: 'Could not change that.' }));
    }
  }

  /** What was written, made into memories by the bot's memory model. */
  protected async teach(): Promise<void> {
    const botId = this.botId();
    const text = this.lesson().trim();
    if (!botId || !text || this.teaching()) return;
    this.teaching.set(true);
    try {
      const learned = await firstValueFrom(this.api.apiBotsBotIdMemoriesTeachPost(botId, { text }));
      this.memories.update((list) => [...learned, ...list.filter((m) => !learned.some((l) => l.id === m.id))]);
      this.lesson.set('');
      toast.success(
        learned.length ? `${this.botName()} remembers ${learned.length} new ${learned.length === 1 ? 'thing' : 'things'}.` : `${this.botName()} already knew that.`,
      );
    } catch (error) {
      toast.error(describeApiError(error, { fallback: 'Could not teach that.' }));
    } finally {
      this.teaching.set(false);
    }
  }

  protected async togglePin(memory: BotMemoryDto): Promise<void> {
    await this.update(memory, { pinned: !memory.pinned, text: null });
  }

  protected startEdit(memory: BotMemoryDto): void {
    this.draft.set(memory.text);
    this.editing.set(memory.id);
  }

  protected async saveText(memory: BotMemoryDto): Promise<void> {
    const text = this.draft().trim();
    if (text && text !== memory.text) await this.update(memory, { text, pinned: null });
    this.editing.set(null);
  }

  private async update(memory: BotMemoryDto, changes: { text: string | null; pinned: boolean | null }): Promise<void> {
    try {
      const updated = await firstValueFrom(this.api.apiBotsBotIdMemoriesMemoryIdPatch(memory.botId, memory.id, changes));
      this.memories.update((list) => list.map((m) => (m.id === memory.id ? updated : m)));
    } catch (error) {
      toast.error(describeApiError(error, { fallback: 'Could not change that memory.' }));
    }
  }

  protected async forget(memory: BotMemoryDto): Promise<void> {
    try {
      await firstValueFrom(this.api.apiBotsBotIdMemoriesMemoryIdDelete(memory.botId, memory.id));
      this.memories.update((list) => list.filter((m) => m.id !== memory.id));
    } catch (error) {
      toast.error(describeApiError(error, { fallback: 'Could not forget that.' }));
    }
  }

  protected async clear(): Promise<void> {
    const botId = this.botId();
    if (!botId) return;
    try {
      const count = await firstValueFrom(this.api.apiBotsBotIdMemoriesDelete(botId));
      this.memories.set([]);
      this.hidden.set(0);
      toast.success(`Forgot ${count} ${count === 1 ? 'thing' : 'things'}.`);
    } catch (error) {
      toast.error(describeApiError(error, { fallback: 'Could not clear the memory.' }));
    }
  }
}
