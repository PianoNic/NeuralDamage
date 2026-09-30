import { computed, inject, Service } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { priceTier, type SelectorModel } from '@prompt-kit/model-selector';
import { BotsService } from '../../api/api/bots.service';
import { OpenRouterModel } from '../../core/models';
import { providerIconUrl } from '../../shared/provider-icon';

/** Makers listed first, in this order; the rest follow alphabetically. */
const LEADING_MAKERS = ['Anthropic', 'OpenAI', 'Google', 'xAI', 'Mistral', 'DeepSeek', 'Meta'];

/** Makers whose OpenRouter prefix doesn't read well as a name. */
const MAKER_NAMES: Readonly<Record<string, string>> = {
  openai: 'OpenAI',
  'x-ai': 'xAI',
  'meta-llama': 'Meta',
  mistralai: 'Mistral',
  deepseek: 'DeepSeek',
  moonshotai: 'Moonshot',
  'z-ai': 'Z.ai',
  qwen: 'Qwen',
  minimax: 'MiniMax',
};

/**
 * The OpenRouter models a bot can run on, shaped for prompt-kit's model selector. Taken over from
 * Tessaly's picker; issue #54 wires it into the New bot flow.
 */
@Service()
export class ModelCatalog {
  private readonly api = inject(BotsService);
  private readonly resource = rxResource({ stream: () => this.api.apiBotsModelsGet() });

  readonly models = computed<readonly OpenRouterModel[]>(() =>
    this.resource.hasValue() ? [...this.resource.value()].sort(byMakerThenName) : [],
  );
  readonly loading = this.resource.isLoading;

  readonly selectorModels = computed<readonly SelectorModel[]>(() =>
    this.models().map((model) => ({
      id: model.id,
      name: displayName(model),
      maker: makerOf(model),
      iconUrl: providerIconUrl(model),
      priceTier: isPriced(model)
        ? priceTier(model.pricing!.prompt, model.pricing!.completion)
        : undefined,
      costLabel: isPriced(model)
        ? `$${model.pricing!.prompt} / $${model.pricing!.completion} per 1M tokens`
        : undefined,
    })),
  );

  nameOf(id: string | null | undefined): string {
    const model = this.models().find((candidate) => candidate.id === id);
    return model ? displayName(model) : (id ?? '');
  }
}

function byMakerThenName(a: OpenRouterModel, b: OpenRouterModel): number {
  const rank = (maker: string) => {
    const index = LEADING_MAKERS.indexOf(maker);
    return index === -1 ? LEADING_MAKERS.length : index;
  };
  const makerA = makerOf(a);
  const makerB = makerOf(b);
  return (
    rank(makerA) - rank(makerB) ||
    makerA.localeCompare(makerB) ||
    displayName(a).localeCompare(displayName(b))
  );
}

/** Routers such as openrouter/auto price per request (reported as -1), so there is no tier. */
function isPriced(model: OpenRouterModel): boolean {
  return !!model.pricing && model.pricing.prompt >= 0 && model.pricing.completion >= 0;
}

/** OpenRouter names carry the maker as a prefix ("Anthropic: Claude Sonnet 4"); the picker groups by maker already. */
function displayName(model: OpenRouterModel): string {
  const colon = model.name.indexOf(': ');
  return colon > 0 ? model.name.slice(colon + 2) : model.name;
}

function makerOf(model: OpenRouterModel): string {
  const prefix = model.id.split('/')[0] ?? '';
  if (MAKER_NAMES[prefix]) return MAKER_NAMES[prefix];
  const colon = model.name.indexOf(': ');
  if (colon > 0) return model.name.slice(0, colon);
  return prefix.charAt(0).toUpperCase() + prefix.slice(1);
}
