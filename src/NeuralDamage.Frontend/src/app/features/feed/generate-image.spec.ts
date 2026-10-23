import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { ImagesService } from '../../api/api/images.service';
import { AttachmentsService } from '../../api/api/attachments.service';
import { BotDto, GeneratedImageDto, PostDto } from '../../core/models';
import { ChatList } from '../chat/chat-list';
import { Composer } from '../chat/composer';
import { GenerateImage, ImageDraft, POLL_MS } from './generate-image';
import { PostCard } from './post-card';

const pending = (over: Partial<GeneratedImageDto> = {}): GeneratedImageDto => ({
  id: 'img1',
  status: 'Pending',
  prompt: 'a cat',
  waiting: 1,
  ...over,
});

function draftWith(api: Partial<Record<keyof ImagesService, unknown>>): ImageDraft {
  TestBed.configureTestingModule({ providers: [ImageDraft, { provide: ImagesService, useValue: api }] });
  return TestBed.inject(ImageDraft);
}

describe('ImageDraft', () => {
  afterEach(() => {
    vi.useRealTimers();
    TestBed.resetTestingModule();
  });

  it('polls a picture until it is ready, then hands it to the post once', async () => {
    vi.useFakeTimers();
    const get = vi
      .fn()
      .mockReturnValueOnce(of(pending({ waiting: 2 })))
      .mockReturnValueOnce(of(pending({ status: 'Ready', url: '/api/images/img1/file', waiting: 0 })));
    const draft = draftWith({ apiImagesPost: () => of(pending()), apiImagesImageIdGet: get });

    await draft.generate({ prompt: 'a cat', faceBotId: null });
    expect(draft.pending()).toBe(true);
    expect(draft.take()).toBeNull();

    await vi.advanceTimersByTimeAsync(POLL_MS);
    expect(draft.image()?.waiting).toBe(2);
    await vi.advanceTimersByTimeAsync(POLL_MS);
    expect(draft.ready()).toBe(true);
    expect(get).toHaveBeenCalledTimes(2);

    expect(draft.take()).toBe('img1');
    expect(draft.image()).toBeNull();
    await vi.advanceTimersByTimeAsync(POLL_MS * 3);
    expect(get).toHaveBeenCalledTimes(2);
  });

  it('drops a picture that failed', async () => {
    vi.useFakeTimers();
    const draft = draftWith({
      apiImagesPost: () => of(pending()),
      apiImagesImageIdGet: () => of(pending({ status: 'Failed', error: 'It took too long.' })),
    });

    await draft.generate({ prompt: 'a cat', faceBotId: 'b1' });
    await vi.advanceTimersByTimeAsync(POLL_MS);

    expect(draft.image()).toBeNull();
  });

  it('discards on the server', async () => {
    const remove = vi.fn(() => of(null));
    const draft = draftWith({ apiImagesPost: () => of(pending({ status: 'Ready', url: '/x' })), apiImagesImageIdDelete: remove });

    await draft.generate({ prompt: 'a cat', faceBotId: null });
    await draft.discard();

    expect(remove).toHaveBeenCalledWith('img1');
    expect(draft.image()).toBeNull();
  });

  it('keeps nothing when the generator refuses', async () => {
    const draft = draftWith({ apiImagesPost: () => throwError(() => new Error('nope')) });
    await draft.generate({ prompt: 'a cat', faceBotId: null });
    expect(draft.image()).toBeNull();
  });
});

describe('GenerateImage', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('offers only bots with a picture as a face', async () => {
    const bot = (id: string, name: string, avatarUrl: string | null) => ({ id, name, avatarUrl }) as BotDto;
    await TestBed.configureTestingModule({ imports: [GenerateImage] }).compileComponents();
    const fixture = TestBed.createComponent(GenerateImage);
    fixture.componentRef.setInput('bots', [bot('b1', 'Zed', '/a'), bot('b2', 'Kim', null), bot('b3', 'Gordon', '/c')]);
    const faces = (fixture.componentInstance as unknown as { faces(): BotDto[] }).faces();
    expect(faces.map((b) => b.name)).toEqual(['Gordon', 'Zed']);
  });
});

@Component({
  imports: [Composer],
  template: `<app-composer [members]="[]" [feed]="true" [attachment]="attachment()" (send)="sent.push($event.content)" />`,
})
class Host {
  readonly attachment = signal<'none' | 'busy' | 'ready'>('none');
  readonly sent: string[] = [];
}

describe('Composer with a generated picture', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('sends a ready picture without text, and waits while it is drawn', async () => {
    await TestBed.configureTestingModule({ imports: [Host], providers: [{ provide: AttachmentsService, useValue: {} }] }).compileComponents();
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const send = () => fixture.nativeElement.querySelector('button[aria-label="Send message"]') as HTMLButtonElement;

    expect(send().disabled).toBe(true);
    fixture.componentInstance.attachment.set('ready');
    await fixture.whenStable();
    expect(send().disabled).toBe(false);
    send().click();
    expect(fixture.componentInstance.sent).toEqual(['']);

    fixture.componentInstance.attachment.set('busy');
    const composer = fixture.debugElement.children[0].componentInstance as Composer;
    composer.onValueChange('hello');
    await fixture.whenStable();
    expect(send().disabled).toBe(true);
  });
});

describe('PostCard with a picture', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('shows it at its own aspect ratio, described by its prompt', async () => {
    await TestBed.configureTestingModule({
      imports: [PostCard],
      providers: [provideRouter([]), { provide: ChatList, useValue: { chats: signal([]) } }],
    }).compileComponents();
    const fixture = TestBed.createComponent(PostCard);
    const post: PostDto = {
      id: 'p1',
      author: { id: 'b1', name: 'Gordon', avatarUrl: null, isBot: true, modelId: 'm/x' },
      content: 'dinner',
      sourceChatId: null,
      createdAt: '2026-10-21T10:00:00Z',
      likeCount: 0,
      likedByMe: false,
      commentCount: 0,
      previewComments: [],
      image: { id: 'img1', url: '/api/images/img1/file', width: 1024, height: 768, prompt: 'a burnt lasagna' },
    };
    fixture.componentRef.setInput('post', post);
    await fixture.whenStable();

    const image = fixture.nativeElement.querySelector('[data-testid=post-image]') as HTMLElement;
    expect(image.style.aspectRatio).toBe('1024 / 768');
    expect(image.getAttribute('aria-label')).toContain('a burnt lasagna');
  });
});
