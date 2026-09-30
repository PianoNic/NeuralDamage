import {
  BotFormValue,
  capabilityIcons,
  formatPrice,
  formatPricing,
  hasModelProblem,
  normalizeAliases,
  priceTierMarks,
  toCreateRequest,
  visibilityOptions,
} from './bot-meta';

describe('capabilityIcons', () => {
  it('maps each known capability to its icon and label, in order', () => {
    expect(capabilityIcons(['fast', 'reasoning', 'code', 'vision'])).toEqual([
      { capability: 'fast', icon: 'lucideZap', label: 'Fast' },
      { capability: 'reasoning', icon: 'lucideBrain', label: 'Reasoning' },
      { capability: 'code', icon: 'lucideCode', label: 'Code' },
      { capability: 'vision', icon: 'lucideEye', label: 'Vision' },
    ]);
  });

  it('skips unknown flags and copes with none', () => {
    expect(capabilityIcons(['tools', 'Vision']).map((c) => c.icon)).toEqual(['lucideEye']);
    expect(capabilityIcons(undefined)).toEqual([]);
  });
});

describe('priceTierMarks', () => {
  const lit = (tier: number | null) => priceTierMarks(tier).map((m) => m.on);

  it('lights as many marks as the tier and dims the rest', () => {
    expect(lit(1)).toEqual([true, false, false]);
    expect(lit(2)).toEqual([true, true, false]);
    expect(lit(3)).toEqual([true, true, true]);
  });

  it('clamps out-of-range tiers', () => {
    expect(lit(0)).toEqual([false, false, false]);
    expect(lit(7)).toEqual([true, true, true]);
    expect(lit(null)).toEqual([false, false, false]);
  });
});

describe('formatPrice', () => {
  it('shows cents when exact, otherwise three significant digits', () => {
    expect(formatPrice(0.07)).toBe('$0.07');
    expect(formatPrice(0.1)).toBe('$0.10');
    expect(formatPrice(0.035)).toBe('$0.035');
    expect(formatPrice(0)).toBe('$0.00');
    expect(formatPrice(2.5)).toBe('$2.50');
    expect(formatPrice(4e-7 * 1_000_000)).toBe('$0.40');
    expect(formatPrice(0.396)).toBe('$0.396');
    expect(formatPrice(0.0198)).toBe('$0.0198');
  });

  it('reports variable pricing', () => {
    expect(formatPrice(-1)).toBe('varies');
    expect(formatPricing({ prompt: -1, completion: -1 })).toBeNull();
    expect(formatPricing({ prompt: 0.07, completion: 0.28 })).toBe('$0.07 / $0.28');
  });
});

describe('hasModelProblem', () => {
  it('flags missing and refused models only', () => {
    expect(hasModelProblem('missing')).toBe(true);
    expect(hasModelProblem('notAllowed')).toBe(true);
    expect(hasModelProblem('available')).toBe(false);
    expect(hasModelProblem(null)).toBe(false);
  });
});

describe('visibility rules', () => {
  const value: BotFormValue = {
    name: '  Mona ',
    aliases: 'mo,  mona lisa ,',
    personality: ' Dramatic. ',
    visibility: 'private',
    modelId: 'deepseek/v4-flash',
    temperature: 0.9,
  };

  it('only offers private from a chat', () => {
    expect(visibilityOptions('chat-1')).toEqual(['public', 'private']);
    expect(visibilityOptions(null)).toEqual(['public']);
    expect(visibilityOptions('')).toEqual(['public']);
  });

  it('makes a private bot for the chat it was opened from', () => {
    expect(toCreateRequest(value, 'chat-1')).toEqual({
      name: 'Mona',
      modelId: 'deepseek/v4-flash',
      systemPrompt: '',
      personality: 'Dramatic.',
      aliases: 'mo, mona lisa',
      temperature: 0.9,
      isPublic: false,
      chatId: 'chat-1',
    });
  });

  it('lets a public bot made from a chat join it', () => {
    const request = toCreateRequest({ ...value, visibility: 'public' }, 'chat-1');
    expect(request.isPublic).toBe(true);
    expect(request.chatId).toBe('chat-1');
  });

  it('never sends a private bot without a chat', () => {
    const request = toCreateRequest(value, null);
    expect(request.isPublic).toBe(true);
    expect(request.chatId).toBeNull();
  });

  it('sends a trimmed system prompt, or blank for the server default', () => {
    expect(toCreateRequest({ ...value, systemPrompt: '  Only speak in haiku. ' }, null).systemPrompt).toBe(
      'Only speak in haiku.',
    );
    expect(toCreateRequest({ ...value, systemPrompt: '   ' }, null).systemPrompt).toBe('');
  });

  it('tidies nicknames', () => {
    expect(normalizeAliases(' a , ,b ')).toBe('a, b');
    expect(normalizeAliases('')).toBe('');
  });
});
