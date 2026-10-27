import { Component, computed, DestroyRef, effect, inject, input, output, signal, untracked } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideMusic, lucidePause, lucidePlay, lucideSearch } from '@ng-icons/lucide';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmInputGroupImports } from '@spartan-ng/helm/input-group';
import { HlmSliderImports } from '@spartan-ng/helm/slider';
import { HlmSpinner } from '@spartan-ng/helm/spinner';
import { HlmTabsImports } from '@spartan-ng/helm/tabs';
import { firstValueFrom } from 'rxjs';
import { MusicService } from '../../api';
import { AppConfig } from '../../core/app-config';
import { toastApiError } from '../../core/http-errors';
import type { SongDto, SongResultDto } from '../../core/models';
import { formatTime, MusicPlayer } from './music-player';

/** A clip is this long; the server's `Song.ClipSeconds`. */
export const CLIP_SECONDS = 15;

/** The song picked for a post or story, and where its clip starts in the whole song (null: Deezer's preview). */
export interface PickedSong {
  song: SongDto;
  start: number | null;
}

/** Where a picked song's clip starts at first: a third in, near where choruses tend to be. */
export function defaultStart(song: SongDto): number {
  return Math.max(0, Math.min(Math.round(song.fullSeconds / 3), song.fullSeconds - CLIP_SECONDS));
}

const SEARCH_DELAY_MS = 300;
const POLL_MS = 3000;
/** The whole song is waited for this long; then the preview it is. */
const POLL_TRIES = 40;

/**
 * "Add music": search Deezer, or take a song for the post's words or from the chart, listen to it,
 * pick it, and choose where its 15-second clip starts once the whole song is there.
 */
@Component({
  selector: 'app-music-picker',
  imports: [NgIcon, HlmButton, HlmInputGroupImports, HlmSliderImports, HlmSpinner, HlmTabsImports],
  providers: [provideIcons({ lucideMusic, lucidePause, lucidePlay, lucideSearch })],
  host: { class: 'flex min-h-0 flex-col gap-3' },
  template: `
    <div hlmInputGroup>
      <input
        hlmInputGroupInput
        type="search"
        placeholder="Search songs or artists"
        aria-label="Search music"
        [value]="query()"
        (input)="search($any($event.target).value)"
      />
      <div hlmInputGroupAddon><ng-icon name="lucideSearch" /></div>
    </div>

    @if (!query().trim() && forText().trim()) {
      <hlm-tabs [tab]="tab()" (tabActivated)="tab.set($any($event))" class="min-w-0">
        <hlm-tabs-list class="w-full">
          <button hlmTabsTrigger="for">{{ forLabel() }}</button>
          <button hlmTabsTrigger="trending">Trending</button>
        </hlm-tabs-list>
      </hlm-tabs>
    }

    <div class="-mx-2 flex h-[min(24rem,50dvh)] min-h-0 flex-col gap-0.5 overflow-y-auto" role="list" aria-label="Songs">
      @if (loading() && !results().length) {
        <div class="flex flex-1 items-center justify-center"><hlm-spinner /></div>
      } @else {
        @for (r of results(); track r.deezerId) {
          <div
            role="listitem"
            class="hover:bg-muted flex items-center gap-3 rounded-xl px-2 py-2"
            [class.bg-highlight/15]="picked()?.song?.deezerId === r.deezerId"
            data-testid="song"
          >
            <button type="button" class="flex min-w-0 flex-1 items-center gap-3 text-left" [attr.aria-label]="'Pick ' + r.title + ' by ' + r.artist" (click)="pick(r)">
              @if (r.coverUrl) {
                <img [src]="r.coverUrl" alt="" class="size-12 shrink-0 rounded-md object-cover" loading="lazy" />
              } @else {
                <span class="bg-muted flex size-12 shrink-0 items-center justify-center rounded-md"><ng-icon name="lucideMusic" /></span>
              }
              <span class="flex min-w-0 flex-col">
                <span class="truncate text-sm font-bold">{{ r.title }}</span>
                <span class="text-muted-foreground truncate text-xs">{{ r.artist }} · {{ time(r.durationSeconds) }}</span>
              </span>
            </button>
            @if (r.previewUrl) {
              <button
                hlmBtn
                [variant]="player.isPlaying(rowKey(r)) ? 'default' : 'outline'"
                size="icon-sm"
                class="shrink-0 rounded-full"
                type="button"
                [attr.aria-label]="(player.isPlaying(rowKey(r)) ? 'Stop ' : 'Listen to ') + r.title"
                (click)="listen(r)"
              >
                <ng-icon [name]="player.isPlaying(rowKey(r)) ? 'lucidePause' : 'lucidePlay'" />
              </button>
            }
          </div>
        } @empty {
          <p class="text-muted-foreground px-2 py-8 text-center text-sm">No songs found.</p>
        }
      }
    </div>

    @if (picked(); as p) {
      <div class="border-t pt-3" data-testid="picked-song">
        <div class="flex items-center gap-3">
          @if (p.song.coverUrl) {
            <img [src]="p.song.coverUrl" alt="" class="size-10 shrink-0 rounded-md object-cover" />
          }
          <div class="min-w-0 flex-1">
            <div class="truncate text-sm font-bold">{{ p.song.title }}</div>
            <div class="text-muted-foreground truncate text-xs">{{ p.song.artist }}</div>
          </div>
          <button hlmBtn [variant]="player.isPlaying('picked') ? 'default' : 'outline'" size="icon-sm" class="rounded-full" type="button"
            [attr.aria-label]="player.isPlaying('picked') ? 'Stop the clip' : 'Play the clip'" (click)="playPicked()">
            <ng-icon [name]="player.isPlaying('picked') ? 'lucidePause' : 'lucidePlay'" />
          </button>
        </div>
        @if (p.song.fullUrl && p.start !== null) {
          <div class="mt-2 flex items-center gap-3">
            <hlm-slider
              class="flex-1"
              aria-label="Clip start"
              [min]="0"
              [max]="maxStart(p.song)"
              [step]="1"
              [value]="[p.start]"
              (valueChange)="moveStart($event[0])"
            />
            <span class="text-muted-foreground w-24 text-right text-xs tabular-nums">{{ time(p.start) }} – {{ time(p.start + clip) }}</span>
          </div>
        } @else if (p.song.fullPending && fullSongs()) {
          <p class="text-muted-foreground mt-2 flex items-center gap-2 text-xs"><hlm-spinner class="size-3" /> Getting the whole song, to pick your part…</p>
        } @else {
          <p class="text-muted-foreground mt-2 text-xs">Plays the song's 30-second preview.</p>
        }
      </div>
    }

    <div class="flex justify-end gap-2">
      <button hlmBtn variant="secondary" type="button" (click)="clear()">No music</button>
      <button hlmBtn type="button" [disabled]="!picked() || adding()" (click)="use()">
        @if (adding()) {
          <hlm-spinner />
        }
        {{ picked() ? 'Use ' + picked()!.song.title : 'Pick a song' }}
      </button>
    </div>
  `,
})
export class MusicPicker {
  private readonly api = inject(MusicService);
  protected readonly player = inject(MusicPlayer);
  protected readonly fullSongs = inject(AppConfig).fullSongs;

  /** The post's words, for the songs that fit them. */
  readonly forText = input('');
  readonly forLabel = input('For this post');
  /** The song picked before, to start from. */
  readonly initial = input<PickedSong | null>(null);

  readonly done = output<PickedSong | null>();

  protected readonly clip = CLIP_SECONDS;
  protected readonly query = signal('');
  protected readonly tab = signal<'for' | 'trending'>('trending');
  protected readonly results = signal<SongResultDto[]>([]);
  protected readonly loading = signal(false);
  protected readonly adding = signal(false);
  protected readonly picked = signal<PickedSong | null>(null);

  private searchTimer: ReturnType<typeof setTimeout> | undefined;
  private pollTimer: ReturnType<typeof setTimeout> | undefined;
  private loadSeq = 0;

  protected readonly time = formatTime;
  protected readonly what = computed(() => ({ q: this.query().trim(), tab: this.tab() }));

  constructor() {
    effect(() => {
      const initial = this.initial();
      const text = this.forText().trim();
      untracked(() => {
        this.picked.set(initial);
        this.tab.set(text ? 'for' : 'trending');
      });
    });
    effect(() => {
      const { q, tab } = this.what();
      untracked(() => void this.load(q, tab));
    });
    inject(DestroyRef).onDestroy(() => {
      clearTimeout(this.searchTimer);
      clearTimeout(this.pollTimer);
      this.player.stop();
    });
  }

  protected search(value: string): void {
    clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => this.query.set(value), SEARCH_DELAY_MS);
  }

  protected rowKey(r: SongResultDto): string {
    return `pick:${r.deezerId}`;
  }

  protected maxStart(song: SongDto): number {
    return Math.max(0, song.fullSeconds - CLIP_SECONDS);
  }

  protected listen(r: SongResultDto): void {
    if (r.previewUrl) void this.player.toggle({ key: this.rowKey(r), url: r.previewUrl, start: 0, seconds: 30 });
  }

  /** Stores the song on the server, so its preview is kept and its whole song is fetched. */
  protected async pick(r: SongResultDto): Promise<void> {
    if (this.adding()) return;
    this.adding.set(true);
    try {
      const song = await firstValueFrom(this.api.apiMusicSongsPost({ deezerId: r.deezerId }));
      this.picked.set({ song, start: song.fullUrl ? defaultStart(song) : null });
      this.watch(song);
      void this.playPicked(true);
    } catch (error) {
      toastApiError(error, { network: "Couldn't get that song. Check your connection and try again." });
    } finally {
      this.adding.set(false);
    }
  }

  protected moveStart(start: number): void {
    const p = this.picked();
    if (!p) return;
    this.picked.set({ ...p, start });
    if (this.player.isPlaying('picked')) void this.playPicked(true);
  }

  protected async playPicked(restart = false): Promise<void> {
    const p = this.picked();
    if (!p) return;
    const url = p.start !== null && p.song.fullUrl ? p.song.fullUrl : p.song.previewUrl;
    if (!url) return;
    const clip = { key: 'picked', url, start: p.start ?? 0, seconds: p.start !== null ? CLIP_SECONDS : 30 };
    await (restart ? this.player.play(clip) : this.player.toggle(clip));
  }

  protected clear(): void {
    this.player.stop();
    this.done.emit(null);
  }

  protected use(): void {
    this.player.stop();
    this.done.emit(this.picked());
  }

  private async load(q: string, tab: 'for' | 'trending'): Promise<void> {
    const seq = ++this.loadSeq;
    this.loading.set(true);
    try {
      const forText = !q && tab === 'for' ? this.forText().trim().slice(0, 500) : undefined;
      const found = await firstValueFrom(this.api.apiMusicSearchGet(q || undefined, forText || undefined));
      if (seq === this.loadSeq) this.results.set(found);
    } catch (error) {
      if (seq === this.loadSeq) {
        this.results.set([]);
        toastApiError(error, { network: "Couldn't load songs. Check your connection and try again." });
      }
    } finally {
      if (seq === this.loadSeq) this.loading.set(false);
    }
  }

  /** Asks again until the whole song is there, then lets the clip start anywhere. */
  private watch(song: SongDto, tries = 0): void {
    clearTimeout(this.pollTimer);
    if (!song.fullPending || tries >= POLL_TRIES) return;
    this.pollTimer = setTimeout(async () => {
      const fresh = await firstValueFrom(this.api.apiMusicSongsSongIdGet(song.id)).catch(() => null);
      const p = this.picked();
      if (!fresh || p?.song.id !== song.id) return;
      const arrived = !!fresh.fullUrl && !p.song.fullUrl;
      this.picked.set({ song: fresh, start: fresh.fullUrl ? (p.start ?? defaultStart(fresh)) : null });
      // The preview was playing: switch to the clip of the whole song.
      if (arrived && this.player.isPlaying('picked')) void this.playPicked(true);
      this.watch(fresh, tries + 1);
    }, POLL_MS);
  }
}
