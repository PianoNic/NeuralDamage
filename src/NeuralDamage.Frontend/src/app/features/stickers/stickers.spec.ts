import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Observable, of, Subject } from 'rxjs';
import { StickersService } from '../../api/api/stickers.service';
import { AuthService } from '../../core/auth/auth.service';
import { Message, StickerDto, StickerImportResultDto, StickerLibraryStatusDto, StickerPageDto } from '../../core/models';
import { AvatarImages } from '../../shared/avatar-images';
import { MessageItem } from '../chat/message-item';
import { isAnimated, nameFromFile } from './sticker-file';
import { SEARCH_DEBOUNCE_MS } from './sticker-feed';
import { IMPORT_BATCH_FILES, StickerLibrary } from './sticker-library';
import { StickerPicker } from './sticker-picker';
import { StickersPage } from './stickers-page';

/** Stands in for IntersectionObserver: nothing is on screen until a test says so. */
class FakeObserver {
  static all: FakeObserver[] = [];
  readonly targets = new Set<Element>();
  constructor(private readonly callback: IntersectionObserverCallback) {
    FakeObserver.all.push(this);
  }
  observe(target: Element): void {
    this.targets.add(target);
  }
  unobserve(target: Element): void {
    this.targets.delete(target);
  }
  disconnect(): void {
    this.targets.clear();
  }
  static show(target: Element): void {
    for (const observer of FakeObserver.all.filter((o) => o.targets.has(target)))
      observer.callback([{ target, isIntersecting: true } as IntersectionObserverEntry], observer as unknown as IntersectionObserver);
  }
}
vi.stubGlobal('IntersectionObserver', FakeObserver);

function sticker(id: string, name: string, extra: Partial<StickerDto> = {}): StickerDto {
  return {
    id,
    name,
    url: `/api/stickers/${id}/image`,
    contentType: 'image/webp',
    animated: false,
    description: `${name} description`,
    describing: false,
    useCount: 0,
    uploaderUserId: 'me',
    uploaderName: 'Me',
    createdAt: '2026-10-12T10:00:00Z',
    canEdit: true,
    ...extra,
  };
}

const small = [
  sticker('s1', 'cat-lol', { description: 'cat crying with laughter, for when something is hilarious' }),
  sticker('s2', 'oops', { description: 'monkey covering its eyes, for cringe moments' }),
  sticker('s3', 'fresh', { description: null, describing: true }),
  sticker('s4', 'bare', { description: null, uploaderUserId: 'other', uploaderName: 'Bob', canEdit: false }),
];

/** A library of `size` stickers served a page at a time, searched by description, like the API. */
function fakeApi(library: StickerDto[], extra: Record<string, unknown> = {}) {
  const pages: { search?: string; skip: number; take: number }[] = [];
  const api = {
    pages,
    apiStickersGet: (search: string | undefined, skip = 0, take = 60): Observable<StickerPageDto> => {
      pages.push({ search, skip, take });
      const matching = search ? library.filter((s) => (s.description ?? '').includes(search)) : library;
      return of({ items: matching.slice(skip, skip + take), total: matching.length });
    },
    apiStickersRecentGet: () => of([] as StickerDto[]),
    apiStickersStatusGet: (): Observable<StickerLibraryStatusDto> => of({ count: library.length, describing: 0, canImport: false }),
    apiStickersByIdGet: (ids: string[]) => of(library.filter((s) => ids.includes(s.id))),
    ...extra,
  };
  return api;
}

function providers(api: object) {
  const images = { get: vi.fn((url: string) => signal(`blob:${url}`)) };
  return {
    images,
    list: [
      provideRouter([]),
      { provide: StickersService, useValue: api },
      { provide: AvatarImages, useValue: images },
      { provide: AuthService, useValue: { user: signal({ id: 'me' }), isAdmin: signal(false) } },
    ],
  };
}

const many = (count: number) => Array.from({ length: count }, (_, i) => sticker(`m${i}`, `sticker-${i}`, { description: i % 10 ? 'plain' : 'dog laughing' }));

describe('sticker files', () => {
  it('tells animated GIFs, WebPs and PNGs from still ones', () => {
    const gif = (frames: number) => {
      const bytes = [...'GIF89a'].map((c) => c.charCodeAt(0));
      bytes.push(1, 0, 1, 0, 0, 0, 0);
      for (let i = 0; i < frames; i++) bytes.push(0x2c, 0, 0, 0, 0, 1, 0, 1, 0, 0, 2, 2, 0x44, 1, 0);
      bytes.push(0x3b);
      return new Uint8Array(bytes);
    };
    const webp = (flags: number) => {
      const bytes = new Uint8Array(30);
      bytes.set([...'RIFF'].map((c) => c.charCodeAt(0)), 0);
      bytes.set([...'WEBPVP8X'].map((c) => c.charCodeAt(0)), 8);
      bytes[20] = flags;
      return bytes;
    };

    expect(isAnimated(gif(1))).toBe(false);
    expect(isAnimated(gif(3))).toBe(true);
    expect(isAnimated(webp(0x02))).toBe(true);
    expect(isAnimated(webp(0x10))).toBe(false);
  });

  it('names a sticker after its file', () => {
    expect(nameFromFile('Cat Laughing!.png')).toBe('cat-laughing');
    expect(nameFromFile('Ärger.webp')).toBe('arger');
  });
});

describe('StickerPicker', () => {
  async function setup(api: ReturnType<typeof fakeApi>) {
    const { list, images } = providers(api);
    await TestBed.configureTestingModule({ imports: [StickerPicker], providers: list }).compileComponents();
    const fixture = TestBed.createComponent(StickerPicker);
    await fixture.whenStable();
    await TestBed.inject(StickerLibrary).load();
    await fixture.whenStable();
    return { fixture, images, el: fixture.nativeElement as HTMLElement };
  }

  it('shows Recent first, then the first page of the library', async () => {
    const api = fakeApi(small, { apiStickersRecentGet: () => of([small[1]]) });
    const { el } = await setup(api);

    const recent = el.querySelectorAll('[data-testid="recent"] button');
    expect([...recent].map((b) => b.getAttribute('aria-label'))).toEqual(['Send sticker oops']);
    expect(el.querySelectorAll('[data-testid="all"] button').length).toBe(4);
    expect(el.querySelector('[data-testid="more"]')).toBeNull();
  });

  it('opens on one page of a big library, and reads the next as its end scrolls into view', async () => {
    const api = fakeApi(many(5000));
    const { fixture, el } = await setup(api);

    expect(el.querySelectorAll('[data-testid="all"] button').length).toBe(48);
    expect(api.pages).toEqual([{ search: undefined, skip: 0, take: 48 }]);

    FakeObserver.show(el.querySelector('[data-testid="more"]')!);
    await fixture.whenStable();

    expect(el.querySelectorAll('[data-testid="all"] button').length).toBe(96);
    expect(api.pages.at(-1)).toEqual({ search: undefined, skip: 48, take: 48 });
  });

  it('loads a sticker image only once it comes into view', async () => {
    const { fixture, images, el } = await setup(fakeApi(many(100)));

    const cells = el.querySelectorAll('[data-testid="all"] app-sticker-image');
    expect(cells.length).toBe(48);
    expect(images.get).not.toHaveBeenCalled();
    expect(el.querySelector('[data-testid="all"] img')).toBeNull();

    FakeObserver.show(cells[0]);
    FakeObserver.show(cells[1]);
    await fixture.whenStable();

    expect(images.get.mock.calls.map(([url]) => url)).toEqual(['/api/stickers/m0/image', '/api/stickers/m1/image']);
    expect(el.querySelectorAll('[data-testid="all"] img').length).toBe(2);
    expect(el.querySelector('[data-testid="all"] img')?.getAttribute('loading')).toBe('lazy');
  });

  it('searches on the server once typing pauses, and sends the sticker picked', async () => {
    const api = fakeApi(small);
    const { fixture, el } = await setup(api);
    const picked: StickerDto[] = [];
    fixture.componentInstance.pick.subscribe((s) => picked.push(s));

    const search = el.querySelector('input[type="search"]') as HTMLInputElement;
    for (const typed of ['h', 'hi', 'hilarious']) {
      search.value = typed;
      search.dispatchEvent(new Event('input'));
    }
    await new Promise((resolve) => setTimeout(resolve, SEARCH_DEBOUNCE_MS + 50));
    await fixture.whenStable();

    expect(api.pages.map((p) => p.search)).toEqual([undefined, 'hilarious']);
    const buttons = el.querySelectorAll<HTMLButtonElement>('[data-testid="all"] button');
    expect(buttons.length).toBe(1);
    buttons[0].click();
    expect(picked.map((s) => s.name)).toEqual(['cat-lol']);
  });
});

describe('a sticker message', () => {
  it('shows the sticker large, without a bubble', async () => {
    const { list } = providers(fakeApi(small));
    await TestBed.configureTestingModule({ imports: [MessageItem], providers: list }).compileComponents();
    const fixture = TestBed.createComponent(MessageItem);
    const message: Message = {
      id: 'm1',
      chatId: 'c',
      senderUserId: null,
      senderBotId: 'b1',
      senderName: 'Gus',
      senderAvatar: null,
      senderModelId: null,
      senderType: 'bot',
      content: '',
      mentions: [],
      reactions: [{ emoji: '😂', count: 1, userIds: ['me'], botIds: [], names: ['Me'] }],
      replyTo: null,
      attachments: [],
      sticker: { id: 's1', name: 'cat-lol', url: '/api/stickers/s1/image', animated: false, description: 'cat crying with laughter' },
      story: null,
      createdAt: '2026-10-12T10:00:00Z',
    };
    fixture.componentRef.setInput('message', message);
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;

    const shown = el.querySelector('[data-testid="sticker"]') as HTMLElement;
    FakeObserver.show(shown.querySelector('app-sticker-image') ?? shown);
    await fixture.whenStable();
    expect(shown.style.width).toBe('160px');
    expect(shown.querySelector('img')?.getAttribute('alt')).toBe('cat crying with laughter');
    expect(el.querySelector('.rounded-2xl')).toBeNull();
    // Sender and reactions as on any message.
    expect(el.textContent).toContain('Gus');
    expect(el.textContent).toContain('😂');
  });
});

describe('StickersPage', () => {
  async function setup(api: object) {
    const { list } = providers(api);
    await TestBed.configureTestingModule({ imports: [StickersPage], providers: list }).compileComponents();
    const fixture = TestBed.createComponent(StickersPage);
    await fixture.whenStable();
    await TestBed.inject(StickerLibrary).refresh();
    await fixture.whenStable();
    return { fixture, el: fixture.nativeElement as HTMLElement };
  }

  it('lists the library with each sticker’s state, and edit actions only on yours', async () => {
    const { el } = await setup(fakeApi(small));

    const cards = el.querySelectorAll('[data-testid="sticker-card"]');
    expect(cards.length).toBe(4);
    expect(cards[0].textContent).toContain('cat crying with laughter');
    expect(cards[2].querySelector('[data-testid="describing"]')?.textContent).toContain('Describing');
    expect(cards[3].querySelector('[data-testid="undescribed"]')?.textContent).toContain("bots don't know it");
    expect(cards[3].textContent).toContain('Bob');
    expect(cards[0].querySelector('[aria-label="Actions for cat-lol"]')).not.toBeNull();
    expect(cards[3].querySelector('[aria-label="Actions for bare"]')).toBeNull();
    // Not an admin: no folder import.
    expect(el.textContent).not.toContain('Import folder');
  });

  it('pages a big library: 60 cards, then 60 more as the end scrolls into view', async () => {
    const api = fakeApi(many(5000));
    const { fixture, el } = await setup(api);

    expect(el.querySelectorAll('[data-testid="sticker-card"]').length).toBe(60);
    expect(el.querySelector('[data-testid="sticker-count"]')?.textContent).toContain('5000 stickers');

    FakeObserver.show(el.querySelector('[data-testid="more"]')!);
    await fixture.whenStable();
    expect(el.querySelectorAll('[data-testid="sticker-card"]').length).toBe(120);
  });

  it('imports a folder in batches and shows how far it got', async () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
    try {
      const batches: Subject<StickerImportResultDto>[] = [];
      let describing = 0;
      const api = fakeApi([], {
        apiStickersStatusGet: () => of({ count: 0, describing, canImport: true }),
        apiStickersImportPost: (files: Blob[]) => {
          const batch = new Subject<StickerImportResultDto>();
          batches.push(batch);
          expect(files.length).toBeLessThanOrEqual(IMPORT_BATCH_FILES);
          return batch;
        },
      });
      const { fixture, el } = await setup(api);
      expect(el.textContent).toContain('Import folder');

      // Animated GIFs go as they are, without decoding.
      const gif = new Uint8Array([...'GIF89a'].map((c) => c.charCodeAt(0)).concat([1, 0, 1, 0, 0, 0, 0],
        [0x2c, 0, 0, 0, 0, 1, 0, 1, 0, 0, 2, 2, 0x44, 1, 0], [0x2c, 0, 0, 0, 0, 1, 0, 1, 0, 0, 2, 2, 0x44, 1, 0], [0x3b]));
      const files = Array.from({ length: 120 }, (_, i) => new File([gif], `STK-${i}.gif`, { type: 'image/gif' }));
      files.push(new File(['not a sticker'], 'notes.txt', { type: 'text/plain' }));
      const library = TestBed.inject(StickerLibrary);
      const done = library.importFiles(files);
      const summary = () => el.querySelector('[data-testid="import-summary"]')?.textContent?.trim();

      await vi.waitFor(() => expect(batches.length).toBe(1));
      await fixture.whenStable();
      expect(summary()).toBe('0 / 120 uploaded, 0 described');

      batches[0].next({ added: 48, duplicates: 2, failed: [] });
      batches[0].complete();
      await vi.waitFor(() => expect(batches.length).toBe(2));
      await fixture.whenStable();
      expect(summary()).toBe('50 / 120 uploaded, 0 described');

      batches[1].next({ added: 50, duplicates: 0, failed: [] });
      batches[1].complete();
      await vi.waitFor(() => expect(batches.length).toBe(3));
      batches[2].next({ added: 19, duplicates: 0, failed: [{ fileName: 'STK-119.gif', error: 'Stickers have to be PNG, WebP or GIF images.' }] });
      batches[2].complete();
      await done;

      // The describer has done all but 17 of the 117 added.
      describing = 17;
      await vi.advanceTimersByTimeAsync(3000);
      await fixture.whenStable();
      expect(summary()).toBe('120 / 120 uploaded, 100 described');
      expect(el.querySelector('[data-testid="import-details"]')?.textContent).toContain('117 added · 2 already in the library · 1 refused');
      expect(el.textContent).toContain('STK-119.gif');

      describing = 0;
      await vi.advanceTimersByTimeAsync(3000);
      await fixture.whenStable();
      expect(summary()).toBe('120 / 120 uploaded, 117 described');
    } finally {
      vi.useRealTimers();
    }
  });
});
