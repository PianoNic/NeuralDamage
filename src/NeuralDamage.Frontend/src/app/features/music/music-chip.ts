import { Component, input, output } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideMusic, lucideX } from '@ng-icons/lucide';
import { HlmButton } from '@spartan-ng/helm/button';
import { formatTime } from './music-player';
import type { PickedSong } from './music-picker';

/** The song picked for a post or story being made: its cover, title and clip, to change or take off. */
@Component({
  selector: 'app-music-chip',
  imports: [NgIcon, HlmButton],
  providers: [provideIcons({ lucideMusic, lucideX })],
  host: { class: 'flex' },
  template: `
    <div class="bg-muted flex max-w-full min-w-0 items-center gap-2 rounded-xl py-1 pr-1 pl-1.5" data-testid="music-chip">
      <button type="button" class="flex min-w-0 items-center gap-2 text-left" aria-label="Change the song" (click)="change.emit()">
        @if (picked().song.coverUrl) {
          <img [src]="picked().song.coverUrl" alt="" class="size-8 shrink-0 rounded-md object-cover" />
        } @else {
          <ng-icon name="lucideMusic" class="shrink-0" />
        }
        <span class="flex min-w-0 flex-col leading-tight">
          <span class="truncate text-xs font-bold">{{ picked().song.title }}</span>
          <span class="text-muted-foreground truncate text-[11px]">
            {{ picked().song.artist }}@if (picked().start !== null) { · from {{ time(picked().start!) }}}
          </span>
        </span>
      </button>
      <button hlmBtn variant="ghost" size="icon-xs" type="button" aria-label="Remove the song" (click)="remove.emit()">
        <ng-icon name="lucideX" />
      </button>
    </div>
  `,
})
export class MusicChip {
  readonly picked = input.required<PickedSong>();
  readonly change = output<void>();
  readonly remove = output<void>();

  protected readonly time = formatTime;
}
