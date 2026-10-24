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
 * with live stories, ringed in the mint-to-magenta gradient while some are unseen and grey once all are.
 */
@Component({
  selector: 'app-stories-row',
  imports: [RouterLink, NgIcon, MemberAvatar, StoryViewer],
  providers: [provideIcons({ lucidePlus })],
  host: { class: 'block' },
  template: `
    <section aria-label="Stories" class="flex gap-3 overflow-x-auto py-1 [scrollbar-width:none] [&::-webkit-scrollbar]:hidden">
      <a routerLink="/feed/story" class="flex w-[76px] flex-none flex-col items-center gap-1.5 outline-none" data-testid="new-story">
        <span class="relative flex size-[70px] items-center justify-center">
          <app-member-avatar [name]="auth.displayName() || '?'" [avatarUrl]="auth.user()?.avatarUrl" [px]="64" />
          <span class="bg-primary text-primary-foreground border-background absolute -end-0.5 -bottom-0.5 flex size-6 items-center justify-center rounded-lg border-2">
            <ng-icon name="lucidePlus" size="14" />
          </span>
        </span>
        <span class="text-muted-foreground text-xs">Your story</span>
      </a>
      @for (ring of store.rings(); track ring.author.id; let i = $index) {
        <button
          type="button"
          class="flex w-[76px] flex-none flex-col items-center gap-1.5 rounded-lg outline-none focus-visible:ring-2 focus-visible:ring-ring"
          [attr.aria-label]="'Stories by ' + ring.author.name + (ring.allSeen ? '' : ', unseen')"
          data-testid="story-ring"
          [attr.data-seen]="ring.allSeen"
          (click)="open.set(i)"
        >
          <span
            class="flex size-[70px] items-center justify-center rounded-full p-[3px]"
            [class.bg-border]="ring.allSeen"
            [style.background]="ring.allSeen ? null : 'var(--story-ring)'"
            data-testid="story-ring-border"
            [attr.data-ring]="ring.allSeen ? 'seen' : 'unseen'"
          >
            <span class="bg-background flex rounded-full p-[2.5px]">
              <app-member-avatar [name]="ring.author.name" [avatarUrl]="ring.author.avatarUrl" [modelId]="ring.author.modelId" [px]="59" />
            </span>
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
