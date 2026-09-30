import { OpenRouterModel } from '../../core/models';
import { modelMatches } from './model-catalog';

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
