import { computed, DestroyRef, effect, ElementRef, inject, Injectable, Signal, signal, untracked } from '@angular/core';
import { StickerDto } from '../../core/models';
import { StickerLibrary } from './sticker-library';

/** How long typing pauses before the search goes to the server. */
export const SEARCH_DEBOUNCE_MS = 250;

/** How often stickers on screen that are still being described are read again. */
const DESCRIBING_POLL_MS = 3000;

/**
 * A grid's view of the library: the pages read so far, newest first, for the current search. The
 * grid asks for the next page when its end scrolls into view. Provided per component, so the
 * picker and the Stickers page each have their own.
 */
@Injectable()
export class StickerFeed {
  private readonly library = inject(StickerLibrary);

  readonly items = signal<StickerDto[]>([]);
  /** How many match the search, across all pages. */
  readonly total = signal(0);
  readonly query = signal('');
  /** The search the items shown are for: the query, trimmed, once its first page has arrived. */
  readonly searched = signal('');
  readonly loading = signal(false);
  /** The first page for the current search has arrived. */
  readonly ready = signal(false);
  readonly failed = signal(false);
  readonly hasMore = computed(() => this.items().length < this.total());

  pageSize = 60;

  /** Bumped by every new search, so an answer to an older one is dropped. */
  private generation = 0;
  private debounce: ReturnType<typeof setTimeout> | null = null;
  private poll: ReturnType<typeof setTimeout> | null = null;

  constructor() {
    let first = true;
    effect(() => {
      this.library.changed();
      if (first) {
        first = false;
        return;
      }
      untracked(() => void this.reset());
    });
    inject(DestroyRef).onDestroy(() => {
      if (this.debounce) clearTimeout(this.debounce);
      if (this.poll) clearTimeout(this.poll);
      this.generation++;
    });
  }

  /** What the user typed; it is searched once they pause. */
  search(query: string): void {
    this.query.set(query);
    if (this.debounce) clearTimeout(this.debounce);
    this.debounce = setTimeout(() => void this.reset(), SEARCH_DEBOUNCE_MS);
  }

  /** Reads the first page again, for the current search. */
  async reset(): Promise<void> {
    const generation = ++this.generation;
    const query = this.query().trim();
    this.loading.set(true);
    try {
      const page = await this.library.page(query, 0, this.pageSize);
      if (generation !== this.generation) return;
      this.searched.set(query);
      this.items.set(page.items);
      this.total.set(page.total);
      this.failed.set(false);
    } catch {
      if (generation === this.generation) this.failed.set(true);
    } finally {
      if (generation === this.generation) {
        this.loading.set(false);
        this.ready.set(true);
        this.schedulePoll();
      }
    }
  }

  /** Appends the next page, unless one is on its way or there is none. */
  async more(): Promise<void> {
    if (this.loading() || !this.hasMore()) return;
    const generation = this.generation;
    this.loading.set(true);
    try {
      const page = await this.library.page(this.searched(), this.items().length, this.pageSize);
      if (generation !== this.generation) return;
      // Stickers added since the first page shift the rest down; never show one twice.
      const seen = new Set(this.items().map((s) => s.id));
      this.items.update((list) => [...list, ...page.items.filter((s) => !seen.has(s.id))]);
      this.total.set(page.total);
    } catch {
      // The sentinel asks again when it next comes into view.
    } finally {
      if (generation === this.generation) {
        this.loading.set(false);
        this.schedulePoll();
      }
    }
  }

  /** A sticker this user just added goes first. */
  prepend(sticker: StickerDto): void {
    this.items.update((list) => [sticker, ...list.filter((s) => s.id !== sticker.id)]);
    this.total.update((n) => n + 1);
    this.schedulePoll();
  }

  replace(sticker: StickerDto): void {
    this.items.update((list) => list.map((s) => (s.id === sticker.id ? sticker : s)));
  }

  remove(id: string): void {
    if (!this.items().some((s) => s.id === id)) return;
    this.items.update((list) => list.filter((s) => s.id !== id));
    this.total.update((n) => Math.max(0, n - 1));
  }

  /** While stickers read so far are still being described, reads those again every few seconds. */
  private schedulePoll(): void {
    if (this.poll) clearTimeout(this.poll);
    this.poll = null;
    if (!this.items().some((s) => s.describing)) return;
    const generation = this.generation;
    this.poll = setTimeout(async () => {
      const ids = this.items().filter((s) => s.describing).map((s) => s.id);
      try {
        const fresh = await this.library.byIds(ids);
        if (generation !== this.generation) return;
        const byId = new Map(fresh.map((s) => [s.id, s]));
        this.items.update((list) => list.map((s) => byId.get(s.id) ?? s));
      } catch {
        // Tried again on the next round.
      }
      if (generation === this.generation) this.schedulePoll();
    }, DESCRIBING_POLL_MS);
  }
}

/**
 * In a component's constructor: a new search starts at the top of the scroll container, so the
 * end of the old, longer list does not load the new search's second page straight away.
 */
export function scrollToTopOnSearch(feed: StickerFeed, scroller: Signal<ElementRef<HTMLElement> | undefined>): void {
  effect(() => {
    feed.searched();
    untracked(() => scroller()?.nativeElement.scrollTo?.({ top: 0 }));
  });
}
