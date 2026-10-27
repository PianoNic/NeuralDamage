import { Component, computed, DestroyRef, inject, input } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideMusic, lucidePause } from '@ng-icons/lucide';
import type { MusicDto } from '../../core/models';
import { clipOf, MusicPlayer } from './music-player';

/**
 * The song under a post's or story's author, like Instagram's: a note, the title and the artist. Tapping
 * it plays the clip, or stops it.
 */
@Component({
  selector: 'app-music-line',
  imports: [NgIcon],
  providers: [provideIcons({ lucideMusic, lucidePause })],
  host: { class: 'flex min-w-0' },
  template: `
    <button
      type="button"
      class="flex min-w-0 items-center gap-1 text-xs underline-offset-2 hover:underline"
      [attr.aria-label]="(playing() ? 'Stop ' : 'Play ') + music().title + ' by ' + music().artist"
      [attr.aria-pressed]="playing()"
      data-testid="music-line"
      (click)="player.toggle(clip())"
    >
      <ng-icon [name]="playing() ? 'lucidePause' : 'lucideMusic'" size="12" class="shrink-0" />
      <span class="truncate">{{ music().title }} · {{ music().artist }}</span>
      @if (playing()) {
        <span class="flex h-2.5 shrink-0 items-end gap-px" aria-hidden="true">
          <span class="music-bar w-0.5 bg-current"></span>
          <span class="music-bar w-0.5 bg-current [animation-delay:0.3s]"></span>
          <span class="music-bar w-0.5 bg-current [animation-delay:0.6s]"></span>
        </span>
      }
    </button>
  `,
})
export class MusicLine {
  protected readonly player = inject(MusicPlayer);

  readonly music = input.required<MusicDto>();
  /** What it belongs to, a post's or story's id, so only its own line shows "playing". */
  readonly key = input.required<string>();

  protected readonly clip = computed(() => clipOf(this.key(), this.music()));
  protected readonly playing = computed(() => this.player.playing() === this.key());

  constructor() {
    // A post that leaves the screen (another page, a deleted post) takes its song with it.
    inject(DestroyRef).onDestroy(() => this.player.stop(this.key()));
  }
}
