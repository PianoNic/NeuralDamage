import { computed, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { OpenRouterModel } from '../../core/models';
import { ModelBrowser } from './model-browser';
import { ModelCatalog, recommendedModels } from './model-catalog';

const base: Omit<OpenRouterModel, 'id' | 'name' | 'provider'> = {
  capabilities: [],
  priceTier: 1,
  isCustom: false,
  isDefault: false,
  pricing: { prompt: 0.1, completion: 0.5 },
};

const vllm: OpenRouterModel = {
  ...base,
  id: 'vllm:deepseek-chat',
  name: 'vLLM: deepseek-chat',
  provider: 'vLLM',
  isCustom: true,
  isDefault: true,
  pricing: { prompt: 0, completion: 0 },
  recommendationRank: 0,
  recommendationHint: 'Best all-rounder for any character',
};
const luna: OpenRouterModel = {
  ...base,
  id: 'openai/gpt-6-luna',
  name: 'OpenAI: GPT-6 Luna',
  provider: 'OpenAI',
  description: 'A small OpenAI model.',
  recommendationRank: 1,
  recommendationHint: 'Ultra-short, snappy texting replies',
};
const plain: OpenRouterModel = {
  ...base,
  id: 'qwen/qwen3-32b',
  name: 'Qwen: Qwen3 32B',
  provider: 'Qwen',
  description: 'A dense Qwen model.',
};

function render(models: OpenRouterModel[]) {
  const list = signal(models);
  TestBed.configureTestingModule({
    providers: [
      {
        provide: ModelCatalog,
        useValue: {
          models: list,
          loading: signal(false),
          failed: signal(false),
          providers: computed(() => [...new Set(list().map((m) => m.provider))].map((name) => ({ name, iconUrl: '' }))),
          recommended: computed(() => recommendedModels(list())),
        },
      },
    ],
  });
  const fixture = TestBed.createComponent(ModelBrowser);
  fixture.detectChanges();
  const el = fixture.nativeElement as HTMLElement;
  const rows = () =>
    [...el.querySelectorAll('[role="option"]')].map((row) => row.textContent!.replace(/\s+/g, ' ').trim());
  const search = (query: string) => {
    const input = el.querySelector('input[type="search"]') as HTMLInputElement;
    input.value = query;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  };
  return { el, rows, search };
}

describe('ModelBrowser', () => {
  it('opens on Recommended: the default first, each with its "choose this when" line', () => {
    const { el, rows } = render([plain, luna, vllm]);

    const current = el.querySelector('nav [aria-current="true"]');
    expect(current?.textContent?.trim()).toBe('Recommended');
    expect(rows()).toHaveLength(2);
    expect(rows()[0]).toContain('deepseek-chat');
    expect(rows()[0]).toContain('Default · Best all-rounder for any character');
    expect(rows()[1]).toContain('GPT-6 Luna');
    expect(rows()[1]).toContain('Ultra-short, snappy texting replies');
    expect(rows()[1]).not.toContain('A small OpenAI model.');
  });

  it('prices a model that costs nothing as Free', () => {
    const { rows } = render([vllm, luna]);

    expect(rows()[0]).toContain('Free');
    expect(rows()[0]).not.toContain('$0.00');
    expect(rows()[1]).toContain('$0.10 / $0.50');
  });

  it('lets a search override the Recommended view, across every model', () => {
    const { rows, search } = render([plain, luna, vllm]);

    search('qwen');
    expect(rows()).toHaveLength(1);
    expect(rows()[0]).toContain('Qwen3 32B');
    expect(rows()[0]).toContain('A dense Qwen model.');

    search('');
    expect(rows()).toHaveLength(2);
  });

  it('opens on All when nothing is recommended', () => {
    const { el, rows } = render([{ ...plain }]);

    expect(el.querySelector('nav')?.textContent).not.toContain('Recommended');
    expect(el.querySelector('nav [aria-current="true"]')?.textContent?.trim()).toBe('All');
    expect(rows()).toHaveLength(1);
  });
});
