import { afterNextRender, Component, computed, DestroyRef, effect, ElementRef, inject, input, output, signal, untracked, viewChild } from '@angular/core';
import { Router } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideChevronLeft, lucideChevronRight, lucideHeart, lucideSend, lucideTrash2, lucideX } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmInput } from '@spartan-ng/helm/input';
import { AuthService } from '../../core/auth/auth.service';
import { toastApiError } from '../../core/http-errors';
import { StoryDto, StoryRingDto } from '../../core/models';
import { AvatarImages } from '../../shared/avatar-images';
import { MemberAvatar } from '../../shared/member-avatar';
import { formatAgo } from '../feed/feed-model';
import { StoryStore } from './story-store';

/** How long one story stays up before the next. */
export const STORY_MS = 6000;
/** A press longer than this is "hold to look", not a tap: it pauses, and letting go does not skip. */
export const HOLD_MS = 250;
export const QUICK_REACTIONS = ['😂', '🔥', '💀', '😍'] as const;

/** Where tapping through goes from (ring, story): the next story, the next ring, or null at the end. */
export function step(rings: readonly StoryRingDto[], ring: number, story: number, by: 1 | -1): { ring: number; story: number } | null {
  const next = story + by;
  if (next >= 0 && next < (rings[ring]?.stories.length ?? 0)) return { ring, story: next };
  const nextRing = ring + by;
  if (nextRing < 0 || nextRing >= rings.length) return null;
  return { ring: nextRing, story: by === 1 ? 0 : rings[nextRing].stories.length - 1 };
}

/**
 * The full-screen viewer: the story edge to edge with the progress bars and who and when on top, tap
 * left or right to go back or on, and "Reply to X" with a heart for quick reactions. Replies go to a
 * direct chat with them.
 */
@Component({
  selector: 'app-story-viewer',
  imports: [NgIcon, HlmButton, HlmInput, MemberAvatar],
  providers: [provideIcons({ lucideChevronLeft, lucideChevronRight, lucideHeart, lucideSend, lucideTrash2, lucideX })],
  host: {
    class: 'fixed inset-0 z-50 flex items-center justify-center bg-black text-white',
    role: 'dialog',
    'aria-modal': 'true',
    '[attr.aria-label]': '"Story by " + (story()?.author?.name ?? "")',
    '(keydown.escape)': 'close.emit()',
    '(keydown.arrowright)': 'go(1)',
    '(keydown.arrowleft)': 'go(-1)',
  },
  template: `
    @if (story(); as s) {
      <div class="relative flex h-full max-h-[min(100svh,900px)] w-full max-w-[min(100vw,480px)] flex-col sm:py-4" data-testid="story-viewer">
        <div class="relative min-h-0 flex-1 overflow-hidden sm:rounded-2xl">
          @if (src(); as url) {
            <img [src]="url" [alt]="s.description || 'Story by ' + s.author.name" class="absolute inset-0 size-full object-cover" data-testid="story-image" />
          } @else {
            <div class="absolute inset-0 animate-pulse bg-white/10"></div>
          }
          <div class="pointer-events-none absolute inset-x-0 top-0 h-36 bg-linear-to-b from-black/60 to-transparent"></div>

          <!-- Tap left or right to go back or on; press and hold anywhere to pause and look. -->
          <button type="button" class="absolute inset-y-0 left-0 w-1/3 select-none [-webkit-touch-callout:none]" aria-label="Previous story"
            (pointerdown)="hold()" (pointerup)="release()" (pointercancel)="release()" (pointerleave)="release()"
            (contextmenu)="$event.preventDefault()" (click)="tap(-1)"></button>
          <button type="button" class="absolute inset-y-0 right-0 w-2/3 select-none [-webkit-touch-callout:none]" aria-label="Next story"
            (pointerdown)="hold()" (pointerup)="release()" (pointercancel)="release()" (pointerleave)="release()"
            (contextmenu)="$event.preventDefault()" (click)="tap(1)"></button>

          <div class="absolute inset-x-2 top-2.5 grid gap-1 transition-opacity duration-200" [class.opacity-0]="holding()" [style.grid-template-columns]="'repeat(' + count() + ', minmax(0, 1fr))'" [attr.aria-label]="'Story ' + (index() + 1) + ' of ' + count()">
            @for (bar of bars(); track $index) {
              <div class="h-[3px] overflow-hidden rounded-full bg-white/35">
                <div class="h-full bg-white" [style.width.%]="bar"></div>
              </div>
            }
          </div>

          <header class="absolute inset-x-3 top-6 flex items-center gap-2.5 transition-opacity duration-200" [class.opacity-0]="holding()">
            <app-member-avatar [name]="s.author.name" [avatarUrl]="s.author.avatarUrl" [modelId]="s.author.modelId" [px]="34" />
            <div class="flex min-w-0 flex-1 items-baseline gap-1.5 text-sm drop-shadow">
              <span class="truncate font-semibold">{{ s.author.name }}</span>
              <span class="shrink-0 text-white/80">· {{ ago(s.createdAt) }}</span>
            </div>
            @if (ring()?.isMine || isAdmin()) {
              <button hlmBtn variant="ghost" size="icon" class="text-white hover:bg-white/10 hover:text-white" aria-label="Delete story" (click)="remove(s)">
                <ng-icon name="lucideTrash2" />
              </button>
            }
            <button #closeButton hlmBtn variant="ghost" size="icon" class="text-white hover:bg-white/10 hover:text-white" aria-label="Close story" (click)="close.emit()">
              <ng-icon name="lucideX" size="24" />
            </button>
          </header>
        </div>

        @if (!ring()?.isMine) {
          @if (reacting()) {
            <div class="absolute inset-x-0 bottom-20 flex justify-center gap-3" role="group" aria-label="Quick reactions">
              @for (emoji of reactions; track emoji) {
                <button
                  type="button"
                  class="flex size-12 items-center justify-center rounded-full bg-white/15 text-2xl backdrop-blur transition-colors hover:bg-white/25"
                  [attr.aria-label]="'React with ' + emoji"
                  (click)="reacting.set(false); send(emoji)"
                >
                  {{ emoji }}
                </button>
              }
            </div>
          }
          <form class="flex items-center gap-2 px-3 pt-3 pb-[max(0.75rem,env(safe-area-inset-bottom))] transition-opacity duration-200" [class.opacity-0]="holding()" (submit)="$event.preventDefault(); send(draft())">
            <label class="sr-only" for="story-reply">Reply to {{ s.author.name }}</label>
            <input
              id="story-reply"
              hlmInput
              class="h-11 flex-1 rounded-full border-white/50 bg-transparent px-4 text-white placeholder:text-white/70"
              [placeholder]="'Reply to ' + firstName() + '…'"
              autocomplete="off"
              [value]="draft()"
              (input)="draft.set($any($event.target).value)"
              (focus)="paused.set(true)"
              (blur)="paused.set(false)"
            />
            <button hlmBtn variant="ghost" size="icon" type="button" class="size-11 text-white hover:bg-white/10 hover:text-white" aria-label="React" [attr.aria-expanded]="reacting()" (click)="reacting.update((open) => !open)">
              <ng-icon name="lucideHeart" size="24" />
            </button>
            <button hlmBtn variant="ghost" size="icon" type="submit" class="size-11 text-white hover:bg-white/10 hover:text-white" aria-label="Send reply" [disabled]="!draft().trim() || sending()">
              <ng-icon name="lucideSend" size="22" />
            </button>
          </form>
        }
      </div>
    }
  `,
})
export class StoryViewer {
  private readonly store = inject(StoryStore);
  /** An admin may delete anyone's story. */
  protected readonly isAdmin = inject(AuthService).isAdmin;
  private readonly images = inject(AvatarImages);
  private readonly router = inject(Router);
  private readonly closeButton = viewChild<ElementRef<HTMLButtonElement>>('closeButton');

  readonly rings = input.required<StoryRingDto[]>();
  /** The ring to open, and its first story the reader has not seen. */
  readonly startRing = input(0);
  readonly close = output<void>();

  protected readonly reactions = QUICK_REACTIONS;
  protected readonly ringIndex = signal(0);
  protected readonly index = signal(0);
  protected readonly elapsed = signal(0);
  /** Paused while the reply box has focus. */
  protected readonly paused = signal(false);
  /** Paused while the story is pressed and held. */
  protected readonly holding = signal(false);
  private holdStart = 0;
  private heldLong = false;
  /** The quick reactions are open above the reply box. */
  protected readonly reacting = signal(false);
  protected readonly draft = signal('');
  protected readonly sending = signal(false);

  protected readonly ring = computed<StoryRingDto | null>(() => this.rings().at(this.ringIndex()) ?? null);
  protected readonly story = computed<StoryDto | null>(() => this.ring()?.stories[this.index()] ?? null);
  protected readonly count = computed(() => this.ring()?.stories.length ?? 0);
  protected readonly bars = computed(() =>
    Array.from({ length: this.count() }, (_, i) => (i < this.index() ? 100 : i > this.index() ? 0 : Math.min(100, (this.elapsed() / STORY_MS) * 100))),
  );
  protected readonly src = computed(() => {
    const s = this.story();
    return s ? this.images.get(s.imageUrl)() : null;
  });
  protected readonly firstName = computed(() => this.story()?.author.name.split(' ')[0] ?? '');

  constructor() {
    effect(() => {
      const start = this.startRing();
      untracked(() => {
        this.ringIndex.set(start);
        const stories = this.rings()[start]?.stories ?? [];
        const firstUnseen = stories.findIndex((s) => !s.seen);
        this.index.set(firstUnseen >= 0 ? firstUnseen : 0);
      });
    });
    // Each story counts as seen once it is on screen.
    effect(() => {
      const s = this.story();
      if (s) untracked(() => void this.store.markSeen(s));
    });

    const tick = 100;
    const timer = setInterval(() => {
      if (this.paused() || this.holding() || !this.src()) return;
      const next = this.elapsed() + tick;
      if (next >= STORY_MS) this.go(1);
      else this.elapsed.set(next);
    }, tick);
    inject(DestroyRef).onDestroy(() => clearInterval(timer));
    afterNextRender(() => this.closeButton()?.nativeElement.focus());
  }

  protected hold(): void {
    this.holding.set(true);
    this.holdStart = performance.now();
    this.heldLong = false;
  }

  protected release(): void {
    if (!this.holding()) return;
    this.heldLong = performance.now() - this.holdStart > HOLD_MS;
    this.holding.set(false);
  }

  /** The click after a press: a short tap skips, letting go after holding does not. */
  protected tap(by: 1 | -1): void {
    if (this.heldLong) {
      this.heldLong = false;
      return;
    }
    this.go(by);
  }

  protected go(by: 1 | -1): void {
    const next = step(this.rings(), this.ringIndex(), this.index(), by);
    this.elapsed.set(0);
    if (!next) {
      if (by === 1) this.close.emit();
      return;
    }
    this.ringIndex.set(next.ring);
    this.index.set(next.story);
  }

  protected ago(at: string): string {
    return formatAgo(at);
  }

  protected async send(content: string): Promise<void> {
    const s = this.story();
    if (!s || !content.trim() || this.sending()) return;
    this.sending.set(true);
    try {
      const chatId = await this.store.reply(s, content.trim());
      this.draft.set('');
      toast.success(`Sent to ${s.author.name}`, {
        action: { label: 'Open chat', onClick: () => void this.router.navigate(['/chats', chatId]) },
      });
    } catch (error) {
      toastApiError(error, { network: 'Not sent. Check your connection and try again.' });
    } finally {
      this.sending.set(false);
    }
  }

  protected async remove(s: StoryDto): Promise<void> {
    try {
      await this.store.remove(s);
      if (!this.ring()) this.close.emit();
      else if (this.index() >= this.count()) this.index.set(Math.max(0, this.count() - 1));
    } catch (error) {
      toastApiError(error);
    }
  }
}
