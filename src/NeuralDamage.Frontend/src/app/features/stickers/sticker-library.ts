import { HttpErrorResponse } from '@angular/common/http';
import { DestroyRef, inject, Service, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { StickersService } from '../../api/api/stickers.service';
import { StickerDto, StickerPageDto } from '../../core/models';
import { formatBytes } from '../chat/attachments';
import { isAnimated, nameFromFile, prepareSticker, STICKER_MAX_BYTES, STICKER_SIDE, STICKER_TYPES, stickerProblem } from './sticker-file';

/** How often the library's status is read again while an import is being described. */
const STATUS_POLL_MS = 3000;

/** One import request carries at most this many files… */
export const IMPORT_BATCH_FILES = 50;
/** …and about this many bytes; the server takes up to 64 MB. */
export const IMPORT_BATCH_BYTES = 24 * 1024 * 1024;

/** How far an import has got. */
export interface ImportProgress {
  /** Stickers picked, without the files that are not images. */
  total: number;
  /** Sent to the server, whatever became of them. */
  uploaded: number;
  added: number;
  duplicates: number;
  failed: number;
  /** Of the added ones, how many the describer has finished with. */
  described: number;
  /** All files are sent; describing may still run. */
  uploadDone: boolean;
  /** Up to a few of the reasons files were refused. */
  errors: string[];
}

/**
 * The app's one shared sticker library: its size for the sidebar, the user's Recent row, and the
 * calls the picker and the Stickers page read it through, a page at a time. The library itself is
 * never loaded whole: it may hold thousands of stickers.
 */
@Service()
export class StickerLibrary {
  private readonly api = inject(StickersService);

  /** How many stickers the library holds. */
  readonly count = signal(0);
  /** Whether the user is an admin, who may import many at once. */
  readonly canImport = signal(false);
  /** The stickers the user sent last, most recent first. */
  readonly recent = signal<StickerDto[]>([]);
  /** The running or last import, until it is dismissed. */
  readonly importing = signal<ImportProgress | null>(null);
  /** Goes up whenever stickers were added in bulk, so the grids read their first page again. */
  readonly changed = signal(0);

  private loaded: Promise<void> | null = null;
  private poll: ReturnType<typeof setTimeout> | null = null;

  constructor() {
    inject(DestroyRef).onDestroy(() => this.stopPolling());
  }

  load(): Promise<void> {
    this.loaded ??= this.refresh();
    return this.loaded;
  }

  /** Reads the library's size and the Recent row again. */
  async refresh(): Promise<void> {
    const [status, recent] = await Promise.all([
      firstValueFrom(this.api.apiStickersStatusGet()).catch(() => null),
      firstValueFrom(this.api.apiStickersRecentGet()).catch(() => null),
    ]);
    if (status) {
      this.count.set(status.count);
      this.canImport.set(status.canImport);
    }
    if (recent) this.recent.set(recent);
  }

  /** One page, newest first, searched on the server by name and description. */
  page(search: string, skip: number, take: number): Promise<StickerPageDto> {
    return firstValueFrom(this.api.apiStickersGet(search.trim() || undefined, skip, take));
  }

  /** The given stickers as they are now (at most 100), e.g. to see whether they are described yet. */
  byIds(ids: string[]): Promise<StickerDto[]> {
    return ids.length ? firstValueFrom(this.api.apiStickersByIdGet(ids.slice(0, 100))) : Promise.resolve([]);
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
    this.count.update((n) => n + 1);
    return sticker;
  }

  async update(id: string, name: string, description: string): Promise<StickerDto> {
    const sticker = await firstValueFrom(this.api.apiStickersStickerIdPut(id, { name, description }));
    this.recent.update((list) => list.map((s) => (s.id === id ? sticker : s)));
    return sticker;
  }

  async remove(id: string): Promise<void> {
    await firstValueFrom(this.api.apiStickersStickerIdDelete(id));
    this.count.update((n) => Math.max(0, n - 1));
    this.recent.update((list) => list.filter((s) => s.id !== id));
  }

  /** After sending one, it moves to the front of Recent. */
  markUsed(sticker: StickerDto): void {
    this.recent.update((list) => [sticker, ...list.filter((s) => s.id !== sticker.id)].slice(0, 16));
  }

  /**
   * Admins: sends many files in batches, as they are where they already fit, so a file the
   * library has is recognised by its bytes and skipped. Progress is in `importing`, and keeps
   * counting the descriptions after the last batch.
   */
  async importFiles(files: File[]): Promise<void> {
    if (this.importing() && !this.importing()!.uploadDone) return;
    const stickers = files.filter((f) => (STICKER_TYPES as readonly string[]).includes(f.type) || /\.(png|webp|gif)$/i.test(f.name));
    const progress = signal<ImportProgress>({
      total: stickers.length,
      uploaded: 0,
      added: 0,
      duplicates: 0,
      failed: 0,
      described: 0,
      uploadDone: false,
      errors: [],
    });
    const set = (change: Partial<ImportProgress> | ((p: ImportProgress) => Partial<ImportProgress>)) => {
      progress.update((p) => ({ ...p, ...(typeof change === 'function' ? change(p) : change) }));
      this.importing.set(progress());
    };
    set({});

    const before = await firstValueFrom(this.api.apiStickersStatusGet()).catch(() => null);
    const baseline = before?.describing ?? 0;
    this.watchDescribing(baseline, () => progress().added, (described) => set({ described }), () => progress().uploadDone);

    const fail = (count: number, reason: string) =>
      set((p) => ({ uploaded: p.uploaded + count, failed: p.failed + count, errors: [...p.errors, reason].slice(-5) }));

    let batch: File[] = [];
    let bytes = 0;
    const send = async () => {
      if (!batch.length) return;
      const sending = batch;
      batch = [];
      bytes = 0;
      try {
        const result = await firstValueFrom(this.api.apiStickersImportPost(sending));
        set((p) => ({
          uploaded: p.uploaded + sending.length,
          added: p.added + result.added,
          duplicates: p.duplicates + result.duplicates,
          failed: p.failed + result.failed.length,
          errors: [...p.errors, ...result.failed.map((f) => `${f.fileName}: ${f.error}`)].slice(-5),
        }));
        this.count.update((n) => n + result.added);
      } catch (error) {
        const reason = error instanceof HttpErrorResponse && typeof error.error === 'string' ? error.error : 'A batch could not be sent.';
        fail(sending.length, reason);
        if (error instanceof HttpErrorResponse && error.status === 403) throw error;
      }
    };

    try {
      for (const file of stickers) {
        let ready: File;
        try {
          ready = await prepareForImport(file);
        } catch {
          fail(1, `${file.name}: could not be read.`);
          continue;
        }
        if (ready.size > STICKER_MAX_BYTES) {
          fail(1, `${file.name}: over ${formatBytes(STICKER_MAX_BYTES)}.`);
          continue;
        }
        if (batch.length >= IMPORT_BATCH_FILES || bytes + ready.size > IMPORT_BATCH_BYTES) await send();
        batch.push(ready);
        bytes += ready.size;
      }
      await send();
    } catch {
      // Not an admin after all; what was sent stays.
    } finally {
      set({ uploadDone: true });
      this.changed.update((n) => n + 1);
    }
  }

  /** Hides a finished import's progress. */
  dismissImport(): void {
    if (this.importing()?.uploadDone) {
      this.importing.set(null);
      this.stopPolling();
    }
  }

  /**
   * Polls the library's status until every sticker the import added is described: the
   * describing count above what it was before the import is what is still waiting.
   */
  private watchDescribing(baseline: number, added: () => number, report: (described: number) => void, uploadDone: () => boolean): void {
    this.stopPolling();
    const tick = async () => {
      const status = await firstValueFrom(this.api.apiStickersStatusGet()).catch(() => null);
      if (status) {
        this.count.set(status.count);
        const waiting = Math.max(0, status.describing - baseline);
        const described = Math.min(added(), Math.max(0, added() - waiting));
        report(described);
        if (uploadDone() && described >= added()) {
          this.poll = null;
          return;
        }
      }
      this.poll = setTimeout(() => void tick(), STATUS_POLL_MS);
    };
    this.poll = setTimeout(() => void tick(), STATUS_POLL_MS);
  }

  private stopPolling(): void {
    if (this.poll) clearTimeout(this.poll);
    this.poll = null;
  }
}

/**
 * A file as an import sends it: animated ones and still ones that already fit go byte for byte,
 * so the server can tell a sticker it already has; anything bigger is scaled down as usual.
 */
export async function prepareForImport(file: File): Promise<File> {
  const bytes = new Uint8Array(await file.arrayBuffer());
  if (isAnimated(bytes)) return file;
  const bitmap = await createImageBitmap(file);
  const fits = bitmap.width <= STICKER_SIDE && bitmap.height <= STICKER_SIDE;
  bitmap.close();
  if (fits) return file;
  const blob = await prepareSticker(file);
  return new File([blob], file.name.replace(/\.\w+$/, '') + '.webp', { type: blob.type });
}
