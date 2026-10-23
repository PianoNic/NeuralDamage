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

/** `vllm:deepseek-chat` is model `deepseek-chat` on the custom provider `vllm`; OpenRouter ids have no colon before the slash. */
export function customModelPart(id: string): string | null {
  const colon = id.indexOf(':');
  const slash = id.indexOf('/');
  return colon > 0 && (slash < 0 || colon < slash) ? id.slice(colon + 1) : null;
}

export function providerIconUrl(model: { id: string }): string {
  const custom = customModelPart(model.id);
  const id = (custom ?? model.id).toLowerCase();
  // "~anthropic/..." is OpenRouter's alias form of the same vendor.
  let vendor = id.split('/')[0].replace(/^~/, '');
  // A custom provider names its models as it likes ("deepseek-chat"), so any known vendor in the id will do.
  if (custom !== null && !VENDORS.has(vendor)) vendor = id.split(/[/:._-]/).find((part) => VENDORS.has(part)) ?? vendor;
  if ((vendor === 'google' || custom !== null) && /gemma/i.test(id)) return '/model-icons/gemma.svg';
  return `/model-icons/${VENDORS.has(vendor) ? vendor : 'unknown'}.svg`;
}
