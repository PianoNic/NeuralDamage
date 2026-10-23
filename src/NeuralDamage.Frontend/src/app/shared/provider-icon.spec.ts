import { customModelPart, providerIconUrl } from './provider-icon';

describe('providerIconUrl', () => {
  it('uses the vendor before the slash for OpenRouter models', () => {
    expect(providerIconUrl({ id: 'deepseek/deepseek-chat' })).toBe('/model-icons/deepseek.svg');
    expect(providerIconUrl({ id: '~anthropic/claude-sonnet-latest' })).toBe('/model-icons/anthropic.svg');
    expect(providerIconUrl({ id: 'someone-new/model' })).toBe('/model-icons/unknown.svg');
  });

  it('finds a known vendor anywhere in a custom provider model id', () => {
    expect(providerIconUrl({ id: 'vllm:deepseek-chat' })).toBe('/model-icons/deepseek.svg');
    expect(providerIconUrl({ id: 'vllm:qwen/qwen3-32b' })).toBe('/model-icons/qwen.svg');
    expect(providerIconUrl({ id: 'local:gemma3:27b' })).toBe('/model-icons/gemma.svg');
    expect(providerIconUrl({ id: 'vllm:bge-m3' })).toBe('/model-icons/unknown.svg');
  });

  it('tells custom model ids from OpenRouter ids', () => {
    expect(customModelPart('vllm:deepseek-chat')).toBe('deepseek-chat');
    expect(customModelPart('meta-llama/llama-3.3-70b-instruct:free')).toBeNull();
    expect(customModelPart('openai/gpt-5')).toBeNull();
  });
});
