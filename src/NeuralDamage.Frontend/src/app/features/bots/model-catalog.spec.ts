import { OpenRouterModel } from '../../core/models';
import { byNameMatchFirst, modelMatches } from './model-catalog';

const flash: OpenRouterModel = {
  id: 'deepseek/deepseek-v4-flash',
  name: 'DeepSeek: V4 Flash',
  provider: 'DeepSeek',
  description: "DeepSeek's quick general model.",
  capabilities: ['fast'],
  priceTier: 1,
};

describe('modelMatches', () => {
  it('matches everything on an empty search, so the list shows straight away', () => {
    expect(modelMatches(flash, '')).toBe(true);
    expect(modelMatches(flash, '   ')).toBe(true);
  });

  it('matches on name, provider, description and capability', () => {
    expect(modelMatches(flash, 'v4')).toBe(true);
    expect(modelMatches(flash, 'deepseek')).toBe(true);
    expect(modelMatches(flash, 'general')).toBe(true);
    expect(modelMatches(flash, 'FAST')).toBe(true);
    expect(modelMatches(flash, 'code')).toBe(false);
  });
});

describe('byNameMatchFirst', () => {
  const small3: OpenRouterModel = {
    ...flash,
    id: 'mistralai/mistral-small-3',
    name: 'Mistral: Mistral Small 3',
    provider: 'Mistral',
    description: '',
  };
  const ministral: OpenRouterModel = {
    ...flash,
    id: 'mistralai/ministral-14b',
    name: 'Mistral: Ministral 3 14B',
    provider: 'Mistral',
    description: 'Comparable to its larger Mistral Small 3.2 24B counterpart.',
  };
  const ids = (models: OpenRouterModel[]) => models.map((m) => m.id);

  it('puts models named like the search ahead of ones that only mention it', () => {
    expect(ids(byNameMatchFirst([ministral, small3], 'mistral small 3'))).toEqual([small3.id, ministral.id]);
  });

  it('keeps the catalog order without a search', () => {
    expect(ids(byNameMatchFirst([ministral, small3], ' '))).toEqual([ministral.id, small3.id]);
  });
});
