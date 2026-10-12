import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { StickersService } from '../../api/api/stickers.service';
import { AuthService } from '../../core/auth/auth.service';
import { Message, StickerDto } from '../../core/models';
import { AvatarImages } from '../../shared/avatar-images';
import { MessageItem } from '../chat/message-item';
import { isAnimated, nameFromFile } from './sticker-file';
import { matchesSticker, StickerLibrary } from './sticker-library';
import { StickerPicker } from './sticker-picker';
import { StickersPage } from './stickers-page';

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

const library = [
  sticker('s1', 'cat-lol', { description: 'cat crying with laughter, for when something is hilarious' }),
  sticker('s2', 'oops', { description: 'monkey covering its eyes, for cringe moments' }),
  sticker('s3', 'fresh', { description: null, describing: true }),
  sticker('s4', 'bare', { description: null, uploaderUserId: 'other', uploaderName: 'Bob', canEdit: false }),
];

function providers(recent: string[] = []) {
  return [
    provideRouter([]),
    {
      provide: StickersService,
      useValue: { apiStickersGet: () => of(library), apiStickersRecentGet: () => of(recent) },
    },
    { provide: AvatarImages, useValue: { get: (url: string) => signal(`blob:${url}`) } },
    { provide: AuthService, useValue: { user: signal({ id: 'me' }) } },
  ];
}

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

  it('searches names and descriptions', () => {
    expect(matchesSticker(library[0], 'laughter')).toBe(true);
    expect(matchesSticker(library[0], 'cat lol')).toBe(true);
    expect(matchesSticker(library[1], 'laughter')).toBe(false);
  });
});

describe('StickerPicker', () => {
  async function setup(recent: string[]) {
    await TestBed.configureTestingModule({ imports: [StickerPicker], providers: providers(recent) }).compileComponents();
    const fixture = TestBed.createComponent(StickerPicker);
    await TestBed.inject(StickerLibrary).reload();
    await fixture.whenStable();
    return fixture;
  }

  it('shows Recent first, then the whole library', async () => {
    const fixture = await setup(['s2']);
    const el = fixture.nativeElement as HTMLElement;

    const recent = el.querySelectorAll('[data-testid="recent"] button');
    expect([...recent].map((b) => b.getAttribute('aria-label'))).toEqual(['Send sticker oops']);
    expect(el.querySelectorAll('[data-testid="all"] button').length).toBe(4);
  });

  it('searches, and sends the sticker picked', async () => {
    const fixture = await setup([]);
    const el = fixture.nativeElement as HTMLElement;
    const picked: StickerDto[] = [];
    fixture.componentInstance.pick.subscribe((s) => picked.push(s));

    const search = el.querySelector('input[type="search"]') as HTMLInputElement;
    search.value = 'hilarious';
    search.dispatchEvent(new Event('input'));
    await fixture.whenStable();

    const buttons = el.querySelectorAll<HTMLButtonElement>('[data-testid="all"] button');
    expect(buttons.length).toBe(1);
    buttons[0].click();
    expect(picked.map((s) => s.name)).toEqual(['cat-lol']);
  });
});

describe('a sticker message', () => {
  it('shows the sticker large, without a bubble', async () => {
    await TestBed.configureTestingModule({ imports: [MessageItem], providers: providers() }).compileComponents();
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
      createdAt: '2026-10-12T10:00:00Z',
    };
    fixture.componentRef.setInput('message', message);
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;

    const shown = el.querySelector('[data-testid="sticker"]') as HTMLElement;
    expect(shown.style.width).toBe('160px');
    expect(shown.querySelector('img')?.getAttribute('alt')).toBe('cat crying with laughter');
    expect(el.querySelector('.rounded-2xl')).toBeNull();
    // Sender and reactions as on any message.
    expect(el.textContent).toContain('Gus');
    expect(el.textContent).toContain('😂');
  });
});

describe('StickersPage', () => {
  it('lists the library with each sticker’s state, and edit actions only on yours', async () => {
    await TestBed.configureTestingModule({ imports: [StickersPage], providers: providers() }).compileComponents();
    const fixture = TestBed.createComponent(StickersPage);
    await fixture.whenStable();
    await TestBed.inject(StickerLibrary).reload();
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;

    const cards = el.querySelectorAll('[data-testid="sticker-card"]');
    expect(cards.length).toBe(4);
    expect(cards[0].textContent).toContain('cat crying with laughter');
    expect(cards[2].querySelector('[data-testid="describing"]')?.textContent).toContain('Describing');
    expect(cards[3].querySelector('[data-testid="undescribed"]')?.textContent).toContain("bots don't know it");
    expect(cards[3].textContent).toContain('Bob');
    expect(cards[0].querySelector('[aria-label="Actions for cat-lol"]')).not.toBeNull();
    expect(cards[3].querySelector('[aria-label="Actions for bare"]')).toBeNull();

    const search = el.querySelector('input[type="search"]') as HTMLInputElement;
    search.value = 'cringe';
    search.dispatchEvent(new Event('input'));
    await fixture.whenStable();
    expect(el.querySelectorAll('[data-testid="sticker-card"]').length).toBe(1);
  });
});
