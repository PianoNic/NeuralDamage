/**
 * An icon for the maker of a model, from the set shipped in `public/model-icons` (see SOURCES.md there):
 * one SVG per OpenRouter vendor, the part of the model id before the slash. Served with the app, so the
 * picker makes no third-party requests. Vendors added to OpenRouter later get the neutral box until
 * their icon is added.
 */
const VENDORS = new Set([
  'aion-labs', 'amazon', 'anthracite-org', 'anthropic', 'arcee-ai', 'baidu', 'bytedance', 'bytedance-seed',
  'cognitivecomputations', 'cohere', 'deepseek', 'dots-studio', 'fireworks', 'google', 'gryphe', 'ibm-granite',
  'inception', 'inclusionai', 'inference-net', 'kwaipilot', 'liquid', 'mancer', 'meituan', 'meta', 'meta-llama',
  'microsoft', 'minimax', 'mistralai', 'moonshotai', 'morph', 'nex-agi', 'nousresearch', 'nvidia', 'openai',
  'openrouter', 'perceptron', 'perplexity', 'poolside', 'prism-ml', 'qwen', 'rekaai', 'relace', 'sakana',
  'sao10k', 'stealth', 'stepfun', 'tencent', 'thedrummer', 'thinkingmachines', 'typesafe', 'unbiased',
  'undi95', 'upstage', 'writer', 'x-ai', 'xiaomi', 'z-ai',
]);

export function providerIconUrl(model: { id: string }): string {
  // "~anthropic/..." is OpenRouter's alias form of the same vendor.
  const vendor = model.id.split('/')[0].replace(/^~/, '').toLowerCase();
  if (vendor === 'google' && /gemma/i.test(model.id)) return '/model-icons/gemma.svg';
  return `/model-icons/${VENDORS.has(vendor) ? vendor : 'unknown'}.svg`;
}
