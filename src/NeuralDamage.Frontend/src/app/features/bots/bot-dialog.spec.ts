import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { BotsService } from '../../api/api/bots.service';
import { BotDialog } from './bot-dialog';

describe('BotDialog', () => {
  const created: unknown[] = [];

  beforeEach(() => {
    created.length = 0;
    TestBed.configureTestingModule({
      providers: [
        {
          provide: BotsService,
          useValue: {
            apiBotsModelsGet: () =>
              of([{ id: 'deepseek/v4', name: 'DeepSeek: V4', provider: 'DeepSeek', priceTier: 1, isDefault: true }]),
            apiBotsPost: (request: unknown) => {
              created.push(request);
              return of({ id: 'b1', avatarUrl: null });
            },
          },
        },
      ],
    });
  });

  async function open() {
    const fixture = TestBed.createComponent(BotDialog);
    fixture.componentRef.setInput('open', true);
    fixture.detectChanges();
    await fixture.whenStable();
    const form = document.querySelector('form[aria-label="Bot"]') as HTMLFormElement;
    return { fixture, form };
  }

  afterEach(() => document.querySelectorAll('.cdk-overlay-container').forEach((el) => (el.innerHTML = '')));

  it('puts the short description and the character up front, with no Advanced section', async () => {
    const { form } = await open();

    const labels = [...form.querySelectorAll('[hlmFieldLabel]')].map((el) => el.textContent?.trim());
    expect(labels).toEqual(['Name', 'Nicknames', 'Short description', 'Character', 'Visibility', 'Model', 'Temperature', 'Posts on the feed']);
    // A public bot posts on the feed by default, a few times a day.
    expect(form.querySelector('[data-testid=posts-per-day]')?.textContent?.trim()).toBe('3');
    expect(form.textContent).not.toContain('Advanced');
    expect(form.textContent).not.toContain('System prompt');
    expect(form.textContent).not.toContain('Personality');

    const description = form.querySelector('#bot-description') as HTMLInputElement;
    expect(description.tagName).toBe('INPUT');
    expect(description.getAttribute('maxlength')).toBe('140');
    expect(description.placeholder).toContain('e.g.');
    expect((form.querySelector('#bot-character') as HTMLTextAreaElement).tagName).toBe('TEXTAREA');
  });

  it('will not create a bot without a short description', async () => {
    const { fixture, form } = await open();
    const name = form.querySelector('#bot-name') as HTMLInputElement;
    name.value = 'Rex';
    name.dispatchEvent(new Event('input'));

    form.dispatchEvent(new Event('submit'));
    fixture.detectChanges();
    await fixture.whenStable();

    expect(created).toEqual([]);
    expect(form.textContent).toContain('Give it a short description.');

    const description = form.querySelector('#bot-description') as HTMLInputElement;
    description.value = 'Retired pirate captain';
    description.dispatchEvent(new Event('input'));
    const character = form.querySelector('#bot-character') as HTMLTextAreaElement;
    character.value = 'You are Rex, a pirate.';
    character.dispatchEvent(new Event('input'));
    form.dispatchEvent(new Event('submit'));
    await fixture.whenStable();

    expect(created).toEqual([
      expect.objectContaining({ name: 'Rex', personality: 'Retired pirate captain', systemPrompt: 'You are Rex, a pirate.' }),
    ]);
  });
});
