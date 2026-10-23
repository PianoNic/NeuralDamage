import { Component, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucidePlus } from '@ng-icons/lucide';
import { AuthService } from '../../core/auth/auth.service';
import { MemberAvatar } from '../../shared/member-avatar';
import { StoryStore } from './story-store';
import { StoryViewer } from './story-viewer';

/**
 * The row of stories on top of the feed: "Your story" to make one, then a circle per person or bot
 * with live stories, ringed in the primary colour while some are unseen and grey once all are.
 */
@Component({
  selector: 'app-stories-row',
  imports: [RouterLink, NgIcon, MemberAvatar, StoryViewer],
  providers: [provideIcons({ lucidePlus })],
  host: { class: 'block' },
  template: `
    <section aria-label="Stories" class="flex gap-4 overflow-x-auto pb-1 [scrollbar-width:thin]">
      <a routerLink="/feed/story" class="flex w-[72px] flex-none flex-col items-center gap-1.5 outline-none" data-testid="new-story">
        <span
          class="border-muted-foreground/50 text-muted-foreground hover:border-foreground hover:text-foreground flex size-16 items-center justify-center rounded-full border-2 border-dashed transition-colors"
        >
          <ng-icon name="lucidePlus" size="24" />
        </span>
        <span class="text-muted-foreground text-xs">Your story</span>
      </a>
      @for (ring of store.rings(); track ring.author.id; let i = $index) {
        <button
          type="button"
          class="flex w-[72px] flex-none flex-col items-center gap-1.5 rounded-lg outline-none focus-visible:ring-2 focus-visible:ring-ring"
          [attr.aria-label]="'Stories by ' + ring.author.name + (ring.allSeen ? '' : ', unseen')"
          data-testid="story-ring"
          [attr.data-seen]="ring.allSeen"
          (click)="open.set(i)"
        >
          <span class="flex size-16 items-center justify-center rounded-full border-2 p-0.5" [class.border-primary]="!ring.allSeen" [class.border-border]="ring.allSeen">
            <app-member-avatar [name]="ring.author.name" [avatarUrl]="ring.author.avatarUrl" [modelId]="ring.author.modelId" [px]="54" />
          </span>
          <span class="w-full truncate text-center text-xs" [class.text-muted-foreground]="ring.allSeen">
            {{ ring.isMine ? 'You' : ring.author.name }}
          </span>
        </button>
      }
    </section>

    @if (open() !== null) {
      <app-story-viewer [rings]="store.rings()" [startRing]="open()!" (close)="open.set(null)" />
    }
  `,
})
export class StoriesRow implements OnInit {
  protected readonly store = inject(StoryStore);
  protected readonly auth = inject(AuthService);
  /** The ring open in the viewer. */
  protected readonly open = signal<number | null>(null);

  constructor() {
    // Expired ones drop out on their own while the feed is open.
    const timer = setInterval(() => this.store.pruneExpired(), 60_000);
    inject(DestroyRef).onDestroy(() => clearInterval(timer));
  }

  ngOnInit(): void {
    void this.store.load();
  }
}
