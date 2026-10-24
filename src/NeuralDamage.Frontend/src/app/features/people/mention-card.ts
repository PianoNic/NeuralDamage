import { afterNextRender, Component, computed, inject, Injector, input, output, signal, viewChild } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideAtSign, lucideMessageCircle, lucideUserRound, lucideVolume2, lucideVolumeX } from '@ng-icons/lucide';
import { BrnPopover } from '@spartan-ng/brain/popover';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmPopoverImports } from '@spartan-ng/helm/popover';
import { ChatMember } from '../../core/models';
import { BotDetails } from '../bots/bot-details';
import { MemberSummary } from './member-summary';

/**
 * The small card a click on an @mention in a message opens, anchored to the mention: who it is, and
 * Mention / Mute / View profile. Escape or a click outside closes it.
 */
@Component({
  selector: 'app-mention-card',
  imports: [NgIcon, HlmButton, HlmPopoverImports, MemberSummary],
  providers: [provideIcons({ lucideAtSign, lucideMessageCircle, lucideUserRound, lucideVolume2, lucideVolumeX })],
  template: `
    <hlm-popover align="start" [sideOffset]="6" [offsetX]="offsetX()" (closed)="onClosed()">
      <hlm-popover-content
        *hlmPopoverPortal="let ctx"
        class="w-80 max-w-[calc(100vw-2rem)] gap-3 p-3"
        data-testid="mention-card"
      >
        @if (member(); as m) {
          <app-member-summary [member]="m" [bot]="bot()" [currentUserId]="currentUserId()" [px]="40" />
          <div class="flex flex-wrap gap-2">
            @if (!isSelf()) {
              <button hlmBtn variant="outline" size="sm" class="flex-1" (click)="mention.emit(m); ctx.close()">
                <ng-icon name="lucideAtSign" />
                Mention
              </button>
            }
            <!-- Out of a group, into the one direct chat with them. A private bot stays in its chat. -->
            @if (!isSelf() && !direct() && (m.memberType !== 'bot' || bot()?.isPublic)) {
              <button hlmBtn variant="outline" size="sm" class="flex-1" (click)="message.emit(m); ctx.close()">
                <ng-icon name="lucideMessageCircle" />
                Message
              </button>
            }
            @if (m.memberType === 'bot') {
              <button hlmBtn variant="outline" size="sm" class="flex-1" (click)="toggleMute.emit(m)">
                <ng-icon [name]="m.isMuted ? 'lucideVolume2' : 'lucideVolumeX'" />
                {{ m.isMuted ? 'Unmute' : 'Mute' }}
              </button>
            }
            <button hlmBtn variant="outline" size="sm" class="flex-1" (click)="viewProfile.emit(m); ctx.close()">
              <ng-icon name="lucideUserRound" />
              View profile
            </button>
          </div>
        }
      </hlm-popover-content>
    </hlm-popover>
  `,
})
export class MentionCard {
  private readonly details = inject(BotDetails);
  private readonly injector = inject(Injector);
  private readonly popover = viewChild.required(BrnPopover);

  readonly members = input.required<ChatMember[]>();
  readonly currentUserId = input<string | null>(null);
  /** Opened in a direct chat, which is already the one with them. */
  readonly direct = input(false);

  readonly mention = output<ChatMember>();
  readonly toggleMute = output<ChatMember>();
  readonly viewProfile = output<ChatMember>();
  readonly message = output<ChatMember>();

  private readonly memberId = signal<string | null>(null);
  protected readonly offsetX = signal(0);
  private anchor: HTMLElement | null = null;
  /** Whether a card is up or still animating out; a new one waits for it to be gone. */
  private shown = false;
  /** Another mention clicked while a card was up: shown once that card is gone. */
  private next: { memberId: string; anchor: HTMLElement } | null = null;

  /** Live from the member list, so a mute shows at once; gone when they leave the chat. */
  protected readonly member = computed(() => this.members().find((m) => m.id === this.memberId()) ?? null);
  protected readonly bot = computed(() => {
    const botId = this.member()?.botId;
    return botId ? (this.details.byId().get(botId) ?? null) : null;
  });
  protected readonly isSelf = computed(() => {
    const userId = this.member()?.userId;
    return !!userId && userId === this.currentUserId();
  });

  /** Opens the card for a member next to `anchor`; a second click on the same mention closes it. */
  show(memberId: string, anchor: HTMLElement): void {
    const member = this.members().find((m) => m.id === memberId);
    if (!member) return;
    if (member.botId) void this.details.loadMissing([member.botId]);

    const popover = this.popover();
    if (this.shown) {
      const sameOne = popover.stateComputed() === 'open' && this.anchor === anchor;
      this.next = sameOne ? null : { memberId, anchor };
      popover.close();
      return;
    }
    this.memberId.set(memberId);
    this.anchor = anchor;
    this.shown = true;
    this.offsetX.set(shiftToFit(anchor.getBoundingClientRect().left, window.innerWidth));
    popover.setOrigin(anchor);
    // Opens once the offset has reached the popover.
    afterNextRender(() => popover.open(), { injector: this.injector });
  }

  /** Whether the card is showing. */
  isOpen(): boolean {
    return this.popover().stateComputed() === 'open';
  }

  protected onClosed(): void {
    this.shown = false;
    const next = this.next;
    this.next = null;
    if (next) this.show(next.memberId, next.anchor);
  }
}

/** The card's width, as its classes set it: 20rem, or the screen less 1rem a side. */
function cardWidth(viewport: number): number {
  return Math.min(320, viewport - 32);
}

/**
 * How far left to move a card that starts at the mention, so it stays 1rem inside the screen's right
 * edge on a phone; 0 when it already fits.
 */
export function shiftToFit(anchorLeft: number, viewport: number): number {
  return Math.min(0, viewport - 16 - cardWidth(viewport) - anchorLeft);
}
