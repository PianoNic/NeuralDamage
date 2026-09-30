import type { CreateBotRequest, ModelPricing } from '../../api';

/** A capability flag from the API (`OpenRouterModel.capabilities`) as the model browser shows it. */
export interface CapabilityIcon {
  capability: string;
  icon: 'lucideZap' | 'lucideBrain' | 'lucideCode' | 'lucideEye';
  label: string;
}

const CAPABILITIES: Readonly<Record<string, Omit<CapabilityIcon, 'capability'>>> = {
  fast: { icon: 'lucideZap', label: 'Fast' },
  reasoning: { icon: 'lucideBrain', label: 'Reasoning' },
  code: { icon: 'lucideCode', label: 'Code' },
  vision: { icon: 'lucideEye', label: 'Vision' },
};

/** The icons for a model's capabilities, in the order the API lists them; unknown flags are skipped. */
export function capabilityIcons(capabilities: readonly string[] | null | undefined): CapabilityIcon[] {
  return (capabilities ?? []).flatMap((capability) => {
    const known = CAPABILITIES[capability.toLowerCase()];
    return known ? [{ capability, ...known }] : [];
  });
}

/** `$`, `$$` or `$$$` as three marks, the ones past the tier dimmed. Out-of-range tiers are clamped. */
export function priceTierMarks(tier: number | null | undefined): { on: boolean }[] {
  const level = Math.min(3, Math.max(0, Math.round(tier ?? 0)));
  return [1, 2, 3].map((mark) => ({ on: mark <= level }));
}

/** A price per 1M tokens: cents when that is exact ("$0.10"), otherwise three significant digits ("$0.0198"). */
export function formatPrice(perMillion: number): string {
  if (!Number.isFinite(perMillion) || perMillion < 0) return 'varies';
  // Per-token prices scaled up to per-1M carry float noise (0.39999999999999997).
  perMillion = Math.round(perMillion * 1e6) / 1e6;
  const cents = Math.round(perMillion * 100) / 100;
  if (cents === perMillion || perMillion >= 1) return `$${perMillion.toFixed(2)}`;
  return `$${Number(perMillion.toPrecision(3))}`;
}

/** "$in / $out", or null when the model has no fixed price. */
export function formatPricing(pricing: ModelPricing | null | undefined): string | null {
  if (!pricing || pricing.prompt < 0 || pricing.completion < 0) return null;
  return `${formatPrice(pricing.prompt)} / ${formatPrice(pricing.completion)}`;
}

/** OpenRouter names carry the provider as a prefix ("DeepSeek: V4 Flash"); the browser groups by provider already. */
export function modelDisplayName(model: { name: string }): string {
  const colon = model.name.indexOf(': ');
  return colon > 0 ? model.name.slice(colon + 2) : model.name;
}

/** Whether a bot's model is gone or refused (`modelStatus` from the API). */
export function hasModelProblem(status: string | null | undefined): boolean {
  return status === 'missing' || status === 'notAllowed';
}

export function modelProblemLabel(status: string | null | undefined): string {
  return status === 'missing' ? 'Model gone' : 'Model blocked';
}

export type Visibility = 'public' | 'private';

/**
 * Which visibilities the form offers. A private bot belongs to one chat, so it is only on offer when
 * the form was opened from a chat; the Bots page has none and only makes public bots.
 */
export function visibilityOptions(chatId: string | null | undefined): Visibility[] {
  return chatId ? ['public', 'private'] : ['public'];
}

export interface BotFormValue {
  name: string;
  aliases: string;
  personality: string;
  visibility: Visibility;
  modelId: string;
  temperature: number;
  /** Optional instructions that replace the default "You are <name>."; blank keeps the default. */
  systemPrompt?: string;
}

/**
 * The create request for the form. Made from a chat, the bot joins that chat whichever visibility it
 * has; a private bot without a chat is not possible and falls back to public.
 */
export function toCreateRequest(value: BotFormValue, chatId: string | null | undefined): CreateBotRequest {
  const isPublic = value.visibility === 'public' || !chatId;
  return {
    name: value.name.trim(),
    modelId: value.modelId,
    // Usually blank: the persona lives in the personality and the server falls back to "You are <name>."
    systemPrompt: value.systemPrompt?.trim() ?? '',
    personality: value.personality.trim() || null,
    aliases: normalizeAliases(value.aliases) || null,
    temperature: value.temperature,
    isPublic,
    chatId: chatId || null,
  };
}

/** "mo,  Mona Lisa ," → "mo, Mona Lisa". */
export function normalizeAliases(aliases: string): string {
  return aliases
    .split(',')
    .map((alias) => alias.trim())
    .filter(Boolean)
    .join(', ');
}
