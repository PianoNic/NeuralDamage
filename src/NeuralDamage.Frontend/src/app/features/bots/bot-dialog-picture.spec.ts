import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { BotsService } from '../../api/api/bots.service';
import { BotPictureSuggestionDto } from '../../api/model/botPictureSuggestionDto';
import { BotDialog, PICTURE_LOOKUP_DELAY } from './bot-dialog';

const ARNOLD: BotPictureSuggestionDto[] = [
  { index: 0, title: 'Arnold Schwarzenegger', sourceUrl: 'https://en.wikipedia.org/wiki/Arnold_Schwarzenegger' },
  { index: 1, title: 'Gym', sourceUrl: 'https://commons.wikimedia.org/wiki/File:Gym.jpg' },
  { index: 2, title: 'Stage', sourceUrl: 'https://commons.wikimedia.org/wiki/File:Stage.jpg' },
];

describe('BotDialog picture suggestions', () => {
  let suggestions: Record<string, BotPictureSuggestionDto[]>;
  let lookups: string[];
  let uploads: Blob[];
  let drawn: number[][];
  const originalUrl = { create: URL.createObjectURL, revoke: URL.revokeObjectURL };

  beforeEach(() => {
    suggestions = { 'Arnold Schwarzenegger': ARNOLD, 'Drunk Uncle': [] };
    lookups = [];
    uploads = [];
    drawn = [];

    let next = 0;
    URL.createObjectURL = () => `blob:test/${next++}`;
    URL.revokeObjectURL = () => undefined;
    // jsdom cannot decode or draw images: a 600x900 portrait and a canvas that records the crop.
    vi.stubGlobal('createImageBitmap', async () => ({ width: 600, height: 900, close: () => undefined }));
    vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockImplementation(
      () => ({ drawImage: (...args: number[]) => drawn.push(args.slice(1)) }) as never,
    );
    vi.spyOn(HTMLCanvasElement.prototype, 'toBlob').mockImplementation((done: BlobCallback) =>
      done(new Blob(['cropped'], { type: 'image/webp' })),
    );

    TestBed.configureTestingModule({
      providers: [
        {
          provide: BotsService,
          useValue: {
            apiBotsModelsGet: () =>
              of([{ id: 'deepseek/v4', name: 'DeepSeek: V4', provider: 'DeepSeek', priceTier: 1, isDefault: true }]),
            apiBotsPost: () => of({ id: 'b1', avatarUrl: null }),
            apiBotsBotIdAvatarPut: (_id: string, blob: Blob) => {
              uploads.push(blob);
              return of({ avatarUrl: '/api/bots/b1/avatar?v=1' });
            },
            apiBotsPictureSuggestionsGet: (name: string) => {
              lookups.push(name);
              return of(suggestions[name] ?? []);
            },
            apiBotsPictureSuggestionsIndexGet: (index: number, name: string) =>
              suggestions[name]?.[index] ? of(new Blob([`${name}#${index}`], { type: 'image/jpeg' })) : throwError(() => new Error('404')),
          },
        },
      ],
    });
  });

  afterEach(() => {
    document.querySelectorAll('.cdk-overlay-container').forEach((el) => (el.innerHTML = ''));
    URL.createObjectURL = originalUrl.create;
    URL.revokeObjectURL = originalUrl.revoke;
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  const settle = () => new Promise((resolve) => setTimeout(resolve, PICTURE_LOOKUP_DELAY + 100));

  async function openWithName(name: string) {
    const fixture = TestBed.createComponent(BotDialog);
    fixture.componentRef.setInput('open', true);
    fixture.detectChanges();
    await fixture.whenStable();
    const form = document.querySelector('form[aria-label="Bot"]') as HTMLFormElement;
    const input = form.querySelector('#bot-name') as HTMLInputElement;
    input.value = name;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    await settle();
    await fixture.whenStable();
    fixture.detectChanges();
    const button = (text: string) =>
      [...form.querySelectorAll('button')].find((b) => b.textContent?.trim() === text || b.getAttribute('aria-label') === text) as
        | HTMLButtonElement
        | undefined;
    const click = async (text: string) => {
      button(text)!.click();
      await fixture.whenStable();
      fixture.detectChanges();
    };
    return { fixture, form, button, click };
  }

  async function fillAndCreate(fixture: { whenStable(): Promise<unknown> }, form: HTMLFormElement) {
    const description = form.querySelector('#bot-description') as HTMLInputElement;
    description.value = 'Bodybuilder and actor';
    description.dispatchEvent(new Event('input'));
    form.dispatchEvent(new Event('submit'));
    await fixture.whenStable();
  }

  it('offers the first picture once the name settles, and uses it, cropped from the top, when accepted', async () => {
    const { fixture, form, button, click } = await openWithName('Arnold Schwarzenegger');

    expect(lookups).toEqual(['Arnold Schwarzenegger']);
    expect(form.textContent).toContain('Suggested picture');
    expect(form.textContent).toContain('Arnold Schwarzenegger · Wikipedia');
    expect(form.querySelector('img[alt^="Suggested picture"]')?.getAttribute('src')).toBe('blob:test/0');

    await click('Use this picture');

    // A 600x900 portrait: the 600px square starts at the top.
    expect(drawn).toEqual([[0, 0, 600, 600, 0, 0, 256, 256]]);
    expect(form.textContent).not.toContain('Suggested picture');
    expect(button('Remove')).toBeTruthy();

    await fillAndCreate(fixture, form);
    expect(uploads.length).toBe(1);
  });

  it('keeps the model icon when the suggestion is refused, and does not offer it again', async () => {
    const { fixture, form, click } = await openWithName('Arnold Schwarzenegger');

    await click('No thanks');

    expect(form.textContent).not.toContain('Suggested picture');
    expect(form.textContent).toContain("Optional. Without one it shows the model's icon.");
    await settle();
    expect(lookups).toEqual(['Arnold Schwarzenegger']);

    await fillAndCreate(fixture, form);
    expect(uploads).toEqual([]);
  });

  it('cycles through the candidates with the arrows', async () => {
    const { form, click } = await openWithName('Arnold Schwarzenegger');
    expect(form.textContent).toContain('1/3');

    await click('Next suggestion');
    expect(form.textContent).toContain('2/3');
    expect(form.textContent).toContain('Gym · Wikimedia Commons');

    await click('Previous suggestion');
    await click('Previous suggestion');
    expect(form.textContent).toContain('3/3');
    expect(form.textContent).toContain('Stage · Wikimedia Commons');
  });

  it('offers nothing for a name with no match', async () => {
    const { form } = await openWithName('Drunk Uncle');

    expect(lookups).toEqual(['Drunk Uncle']);
    expect(form.textContent).not.toContain('Suggested picture');
    expect(form.textContent).toContain("Optional. Without one it shows the model's icon.");
  });
});
