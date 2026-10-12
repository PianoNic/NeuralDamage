import { computed, DestroyRef, inject, Service, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { StickersService } from '../../api/api/stickers.service';
import { StickerDto } from '../../core/models';
import { formatBytes } from '../chat/attachments';
import { nameFromFile, prepareSticker, STICKER_MAX_BYTES, stickerProblem } from './sticker-file';

/** How often the library is read again while a description is being written. */
const DESCRIBING_POLL_MS = 3000;

/** "cat crying" finds a sticker by its name or its description. */
export function matchesSticker(sticker: Pick<StickerDto, 'name' | 'description'>, query: string): boolean {
  const q = query.trim().toLowerCase();
  if (!q) return true;
  return sticker.name.includes(q.replace(/\s+/g, '-')) || (sticker.description ?? '').toLowerCase().includes(q);
}

/**
 * The app's one shared sticker library, for the sidebar count, the composer's picker and the
 * Stickers page. While a sticker is still being described the list is read again every few
 * seconds, so "Describing…" turns into the description without a reload.
 */
@Service()
export class StickerLibrary {
  private readonly api = inject(StickersService);

  readonly stickers = signal<StickerDto[]>([]);
  /** The ids the user sent last, most recent first. */
  readonly recentIds = signal<string[]>([]);
  readonly loading = signal(false);

  readonly recent = computed(() => {
    const byId = new Map(this.stickers().map((s) => [s.id, s]));
    return this.recentIds().flatMap((id) => byId.get(id) ?? []);
  });

  private loaded: Promise<void> | null = null;
  private poll: ReturnType<typeof setTimeout> | null = null;

  constructor() {
    inject(DestroyRef).onDestroy(() => this.stopPolling());
  }

  load(): Promise<void> {
    this.loaded ??= this.reload();
    return this.loaded;
  }

  async reload(): Promise<void> {
    this.loading.set(true);
    try {
      const [stickers, recent] = await Promise.all([
        firstValueFrom(this.api.apiStickersGet()),
        firstValueFrom(this.api.apiStickersRecentGet()).catch(() => this.recentIds()),
      ]);
      this.stickers.set(stickers);
      this.recentIds.set(recent);
    } catch {
      // The count and the picker stay as they were; the Stickers page reports its own failures.
    } finally {
      this.loading.set(false);
      this.schedulePoll();
    }
  }

  /** Prepares and uploads one file; throws a message fit for a toast when it cannot be a sticker. */
  async upload(file: File): Promise<StickerDto> {
    const problem = stickerProblem(file);
    if (problem) throw new Error(problem);
    let blob: Blob;
    try {
      blob = await prepareSticker(file);
    } catch {
      throw new Error(`${file.name || 'That image'} could not be read.`);
    }
    if (blob.size > STICKER_MAX_BYTES)
      throw new Error(`${file.name || 'That sticker'} is over ${formatBytes(STICKER_MAX_BYTES)}.`);

    const sticker = await firstValueFrom(this.api.apiStickersPost(blob, nameFromFile(file.name) || undefined));
    this.stickers.update((list) => [sticker, ...list]);
    this.schedulePoll();
    return sticker;
  }

  async update(id: string, name: string, description: string): Promise<StickerDto> {
    const sticker = await firstValueFrom(this.api.apiStickersStickerIdPut(id, { name, description }));
    this.stickers.update((list) => list.map((s) => (s.id === id ? sticker : s)));
    return sticker;
  }

  async remove(id: string): Promise<void> {
    await firstValueFrom(this.api.apiStickersStickerIdDelete(id));
    this.stickers.update((list) => list.filter((s) => s.id !== id));
  }

  /** After sending one, it moves to the front of Recent. */
  markUsed(id: string): void {
    this.recentIds.update((ids) => [id, ...ids.filter((i) => i !== id)].slice(0, 16));
  }

  private schedulePoll(): void {
    this.stopPolling();
    if (!this.stickers().some((s) => s.describing)) return;
    this.poll = setTimeout(() => void this.reload(), DESCRIBING_POLL_MS);
  }

  private stopPolling(): void {
    if (this.poll) clearTimeout(this.poll);
    this.poll = null;
  }
}
