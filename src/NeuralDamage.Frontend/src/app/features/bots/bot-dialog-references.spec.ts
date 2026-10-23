import { HttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { from, of } from 'rxjs';
import { BotsService } from '../../api/api/bots.service';
import { BotDto } from '../../core/models';
import { BotDialog } from './bot-dialog';

const BOT = {
  id: 'b1',
  name: 'Mia',
  modelId: 'deepseek/v4',
  systemPrompt: 'You are Mia.',
  personality: 'Barista who knows everyone',
  temperature: 0.8,
  isPublic: true,
  feedEnabled: true,
  maxPostsPerDay: 3,
  maxStoriesPerDay: 1,
  modelStatus: 'available',
  looks: null,
  references: [],
} as unknown as BotDto;

describe('BotDialog references', () => {
  let uploads: string[];
  let removed: string[];
  let updates: Record<string, unknown>[];

  beforeEach(() => {
    uploads = [];
    removed = [];
    updates = [];
    URL.createObjectURL = () => 'blob:test/ref';
    TestBed.configureTestingModule({
      providers: [
        // Reference pictures load through the API with the token.
        { provide: HttpClient, useValue: { get: () => from(Promise.resolve(new Blob(['png']))) } },
        {
          provide: BotsService,
          useValue: {
            apiBotsModelsGet: () =>
              of([{ id: 'deepseek/v4', name: 'DeepSeek: V4', provider: 'DeepSeek', priceTier: 1, isDefault: true }]),
            apiBotsBotIdGet: () => of(BOT),
            apiBotsBotIdReferencesPost: (_id: string, file: File) => {
              uploads.push(file.name);
              const references = uploads.map((_, i) => ({ id: `r${i}`, url: `/api/bots/b1/references/r${i}` }));
              return of({ references, looks: 'Red hair in a messy bun, freckles.' });
            },
            apiBotsBotIdReferencesReferenceIdDelete: (_id: string, referenceId: string) => {
              removed.push(referenceId);
              return of(null);
            },
            apiBotsBotIdPut: (_id: string, request: Record<string, unknown>) => {
              updates.push(request);
              return of(null);
            },
          },
        },
      ],
    });
  });

  afterEach(() => document.querySelectorAll('.cdk-overlay-container').forEach((el) => (el.innerHTML = '')));

  async function open(bot: BotDto | null) {
    const fixture = TestBed.createComponent(BotDialog);
    fixture.componentRef.setInput('bot', bot);
    fixture.componentRef.setInput('open', true);
    fixture.detectChanges();
    await fixture.whenStable();
    const form = document.querySelector('form[aria-label="Bot"]') as HTMLFormElement;
    return { fixture, form };
  }

  async function pick(fixture: { detectChanges(): void; whenStable(): Promise<unknown> }, form: HTMLFormElement, name: string) {
    const input = form.querySelector('section input[type=file]') as HTMLInputElement;
    Object.defineProperty(input, 'files', { value: [new File(['x'], name, { type: 'image/png' })], configurable: true });
    input.dispatchEvent(new Event('change'));
    await fixture.whenStable();
    fixture.detectChanges();
  }

  it('asks a new bot to be created first', async () => {
    const { form } = await open(null);

    expect(form.textContent).toContain('Once the bot is created');
    expect(form.querySelector('[aria-label="Add a reference picture"]')).toBeNull();
    expect(form.querySelector('#bot-looks')).toBeNull();
  });

  it('uploads a reference at once and shows the looks written from it, up to three', async () => {
    const { fixture, form } = await open(BOT);

    await pick(fixture, form, 'one.png');
    expect(uploads).toEqual(['one.png']);
    expect((form.querySelector('#bot-looks') as HTMLTextAreaElement).value).toBe('Red hair in a messy bun, freckles.');
    expect(form.querySelectorAll('[role=listitem]').length).toBe(1);

    await pick(fixture, form, 'two.png');
    await pick(fixture, form, 'three.png');
    expect(form.querySelectorAll('[role=listitem]').length).toBe(3);
    expect(form.querySelector('[aria-label="Add a reference picture"]')).toBeNull();

    (form.querySelector('[aria-label="Remove reference 2"]') as HTMLButtonElement).click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(removed).toEqual(['r1']);
    expect(form.querySelectorAll('[role=listitem]').length).toBe(2);
  });

  it('saves the looks as edited', async () => {
    const { fixture, form } = await open({ ...BOT, looks: 'Short black hair.' } as BotDto);
    const looks = form.querySelector('#bot-looks') as HTMLTextAreaElement;
    expect(looks.value).toBe('Short black hair.');

    looks.value = '  Short black hair, silver earrings.  ';
    looks.dispatchEvent(new Event('input'));
    form.dispatchEvent(new Event('submit'));
    await fixture.whenStable();

    expect(updates).toEqual([expect.objectContaining({ looks: 'Short black hair, silver earrings.' })]);
  });
});
