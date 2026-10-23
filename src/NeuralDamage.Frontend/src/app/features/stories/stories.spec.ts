import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, Subject } from 'rxjs';
import { BotsService } from '../../api/api/bots.service';
import { StickersService } from '../../api/api/stickers.service';
import { ImagesService } from '../../api/api/images.service';
import { AppConfig } from '../../core/app-config';
import { ImageDraft } from '../feed/generate-image';
import { StoriesService } from '../../api/api/stories.service';
import { AuthService } from '../../core/auth/auth.service';
import { StoryDto, StoryRingDto } from '../../core/models';
import { SignalRService } from '../../core/signalr/signalr.service';
import { AvatarImages } from '../../shared/avatar-images';
import { StoriesRow } from './stories-row';
import { StoryEditor } from './story-editor';
import {
  addImage,
  addSticker,
  addText,
  draftProblem,
  emptyDraft,
  MAX_LAYERS,
  moveLayer,
  removeLayer,
  reorderLayer,
  StoryDraft,
  toSpec,
  updateLayer,
} from './story-model';
import { withSeen, withStory, withoutStory } from './story-store';
import { step, StoryViewer } from './story-viewer';

const bot = (id: string, name: string) => ({ id, name, avatarUrl: null, isBot: true, modelId: 'm/x' });

function story(id: string, over: Partial<StoryDto> = {}): StoryDto {
  return {
    id,
    author: bot('b1', 'Kim'),
    imageUrl: `/api/stories/${id}/image`,
    createdAt: '2026-10-21T10:00:00Z',
    expiresAt: '2099-01-01T00:00:00Z',
    seen: false,
    description: 'Productivity is up',
    ...over,
  };
}

const ring = (stories: StoryDto[], over: Partial<StoryRingDto> = {}): StoryRingDto => ({
  author: stories[0].author,
  stories,
  allSeen: stories.every((s) => s.seen),
  isMine: false,
  ...over,
});

const settle = async (fixture: { whenStable(): Promise<unknown>; detectChanges(): void }) => {
  for (let i = 0; i < 3; i++) {
    await new Promise((r) => setTimeout(r, 0));
    fixture.detectChanges();
    await fixture.whenStable();
  }
};

function providers(api: Partial<Record<keyof StoriesService, unknown>>, images: Partial<Record<keyof ImagesService, unknown>> = {}) {
  return [
    provideRouter([{ path: 'feed', children: [] }]),
    { provide: StoriesService, useValue: api },
    { provide: BotsService, useValue: { apiBotsPictureSuggestionsGet: () => of([]) } },
    { provide: StickersService, useValue: {} },
    { provide: ImagesService, useValue: images },
    { provide: AvatarImages, useValue: { get: (url: string) => signal(`blob:${url}`) } },
    { provide: AuthService, useValue: { user: signal({ id: 'me', displayName: 'Alice', email: 'a@example.com' }), displayName: signal('Alice') } },
    {
      provide: SignalRService,
      useValue: { onChatEvent: () => undefined, offChatEvent: () => undefined, start: () => Promise.resolve(), reconnected$: new Subject() },
    },
  ];
}

describe('story editor model', () => {
  it('adds, moves, resizes and deletes layers, and keeps them on the story', () => {
    let d = addText(emptyDraft(), 'Hi');
    d = addSticker(d, 'swole-doge', '/api/stickers/s1/image');
    const [text, sticker] = d.layers;
    expect(d.selectedId).toBe(sticker.id);

    d = moveLayer(d, text.id, 140, -5);
    expect([d.layers[0].x, d.layers[0].y]).toEqual([100, 0]);
    d = updateLayer(d, sticker.id, { size: 999, rotation: -8 });
    expect(d.layers[1].size).toBe(360);

    d = reorderLayer(d, sticker.id, -1);
    expect(d.layers.map((l) => l.type)).toEqual(['sticker', 'text']);
    d = removeLayer(d, sticker.id);
    expect(d.layers.map((l) => l.id)).toEqual([text.id]);
    expect(d.selectedId).toBeNull();
  });

  it('writes the spec a bot would send, without editor ids or previews', () => {
    let d: StoryDraft = { ...addText(emptyDraft(), 'Kitchen closed'), background: '#1d3347', backgroundImage: { ref: 'upload:abc', previewUrl: 'blob:x' } };
    d = addSticker(d, 'swole-doge', '/api/stickers/s1/image');
    d = updateLayer(d, d.layers[1].id, { rotation: -8 });
    d = addImage(d, 'wiki:Eiffel Tower#1', '/api/bots/picture-suggestions/1?name=Eiffel%20Tower');

    expect(toSpec(d)).toEqual({
      background: { color: '#1d3347', image: 'upload:abc', fit: 'cover' },
      layers: [
        { type: 'text', text: 'Kitchen closed', x: 50, y: 30, size: 28, color: '#fafafa', weight: 700 },
        { type: 'sticker', name: 'swole-doge', x: 50, y: 60, size: 130, rotation: -8 },
        { type: 'image', image: 'wiki:Eiffel Tower#1', x: 50, y: 50, size: 220 },
      ],
    });
  });

  it('stops at the layer limit and says what is missing', () => {
    let d = emptyDraft();
    expect(draftProblem(d)).toContain('Add some text');
    for (let i = 0; i < MAX_LAYERS + 3; i++) d = addText(d);
    expect(d.layers.length).toBe(MAX_LAYERS);
    d = updateLayer(d, d.layers[0].id, { text: '  ' });
    expect(draftProblem(d)).toContain('empty');
  });
});

describe('stories row model', () => {
  it('adds a new story to its ring, marks seen and drops deleted ones', () => {
    let rings = [ring([story('s1')])];
    rings = withStory(rings, story('s2'), 'me');
    expect(rings[0].stories.map((s) => s.id)).toEqual(['s1', 's2']);
    rings = withStory(rings, story('m1', { author: { id: 'me', name: 'Alice', avatarUrl: null, isBot: false, modelId: null } }), 'me');
    expect(rings[0].isMine).toBe(true);
    rings = withSeen(withSeen(rings, 's1'), 's2');
    expect(rings[1].allSeen).toBe(true);
    expect(withoutStory(rings, 'm1').length).toBe(1);
  });

  it('taps through stories, then rings, and ends after the last', () => {
    const rings = [ring([story('a'), story('b')]), ring([story('c', { author: bot('b2', 'Gordon') })])];
    expect(step(rings, 0, 0, 1)).toEqual({ ring: 0, story: 1 });
    expect(step(rings, 0, 1, 1)).toEqual({ ring: 1, story: 0 });
    expect(step(rings, 1, 0, -1)).toEqual({ ring: 0, story: 1 });
    expect(step(rings, 1, 0, 1)).toBeNull();
  });
});

describe('StoriesRow', () => {
  it('rings unseen stories in the primary colour and seen ones in grey', async () => {
    const rings = [ring([story('s1')]), ring([story('s2', { author: bot('b2', 'Gordon'), seen: true })])];
    await TestBed.configureTestingModule({ imports: [StoriesRow], providers: providers({ apiStoriesGet: () => of(rings) }) }).compileComponents();
    const fixture = TestBed.createComponent(StoriesRow);
    await settle(fixture);
    const el = fixture.nativeElement as HTMLElement;

    const buttons = [...el.querySelectorAll('[data-testid=story-ring]')];
    expect(buttons.map((b) => b.getAttribute('data-seen'))).toEqual(['false', 'true']);
    expect(buttons[0].querySelector('.border-primary')).not.toBeNull();
    expect(el.querySelector('[data-testid=new-story]')?.textContent).toContain('Your story');
  });
});

describe('StoryViewer', () => {
  it('shows the first unseen story, marks it seen, and replies to a direct chat', async () => {
    const seen = vi.fn(() => of(null));
    const reply = vi.fn(() => of({ chatId: 'chat-9' }));
    await TestBed.configureTestingModule({
      imports: [StoryViewer],
      providers: providers({ apiStoriesStoryIdSeenPost: seen, apiStoriesStoryIdReplyPost: reply }),
    }).compileComponents();
    const fixture = TestBed.createComponent(StoryViewer);
    fixture.componentRef.setInput('rings', [ring([story('s1', { seen: true }), story('s2')])]);
    await settle(fixture);
    const el = fixture.nativeElement as HTMLElement;

    expect(el.querySelector('[data-testid=story-image]')?.getAttribute('src')).toBe('blob:/api/stories/s2/image');
    expect(el.getAttribute('aria-label')).toBe('Story by Kim');
    expect(seen).toHaveBeenCalledWith('s2');
    expect(el.textContent).toContain('Replies go to a direct chat with Kim');

    [...el.querySelectorAll<HTMLButtonElement>('[role=group] button')].find((b) => b.textContent?.includes('🔥'))!.click();
    await settle(fixture);
    expect(reply).toHaveBeenCalledWith('s2', { content: '🔥' });
  });
});

describe('StoryEditor', () => {
  it('builds the spec from the stage and posts it', async () => {
    const post = vi.fn((req: { spec: unknown }) => of(story('new', { author: { id: 'me', name: 'Alice', avatarUrl: null, isBot: false, modelId: null } })));
    await TestBed.configureTestingModule({ imports: [StoryEditor], providers: providers({ apiStoriesPost: post }) }).compileComponents();
    const fixture = TestBed.createComponent(StoryEditor);
    await settle(fixture);
    const el = fixture.nativeElement as HTMLElement;
    const editor = fixture.componentInstance;

    // It starts with one text layer; add another and pick a background.
    [...el.querySelectorAll('button')].find((b) => b.textContent?.includes('Add text'))!.click();
    (el.querySelector('[aria-label="Background #1d3347"]') as HTMLButtonElement).click();
    await settle(fixture);
    expect(el.querySelectorAll('[data-testid=stage-layer]').length).toBe(2);
    expect(el.querySelectorAll('[data-testid=layer-row]').length).toBe(2);

    // Delete the selected (new) layer from its settings.
    [...el.querySelectorAll<HTMLButtonElement>('[data-testid=layer-settings] button')].find((b) => b.textContent?.includes('Delete layer'))!.click();
    await settle(fixture);
    expect(el.querySelectorAll('[data-testid=stage-layer]').length).toBe(1);

    // No spec JSON is shown to people; check what gets posted instead.
    expect(el.querySelector('[data-testid=spec-json]')).toBeNull();

    [...el.querySelectorAll('button')].find((b) => b.textContent?.trim() === 'Post story')!.click();
    await settle(fixture);
    const { spec } = post.mock.calls[0][0] as { spec: { background: unknown; layers: unknown[] } };
    expect(spec.background).toEqual({ color: '#1d3347' });
    expect(spec.layers[0]).toMatchObject({ type: 'text', text: 'Hello from my story' });
    expect(editor.draft().layers.length).toBe(1);
  });
});

describe('StoryEditor with image generation', () => {
  it('puts a generated picture in as the background', async () => {
    const generate = vi.fn(() => of({ id: 'img1', status: 'Ready', prompt: 'a beach', waiting: 0, url: '/api/images/img1/file', width: 768, height: 1344 }));
    await TestBed.configureTestingModule({ imports: [StoryEditor], providers: providers({}, { apiImagesPost: generate }) }).compileComponents();
    TestBed.inject(AppConfig).apply({ imageGeneration: true });
    const fixture = TestBed.createComponent(StoryEditor);
    await settle(fixture);
    const el = fixture.nativeElement as HTMLElement;
    expect([...el.querySelectorAll('button')].some((b) => b.textContent?.includes('Generate background'))).toBe(true);

    await fixture.debugElement.injector.get(ImageDraft).generate({ prompt: 'a beach', faceBotId: null, forStory: true });
    await settle(fixture);

    expect(generate).toHaveBeenCalledWith({ prompt: 'a beach', faceBotId: null, forStory: true });
    const json = JSON.parse(el.querySelector('[data-testid=spec-json]')!.textContent!);
    expect(json.background).toEqual({ color: expect.any(String), image: 'generated:img1', fit: 'cover' });
  });
});
