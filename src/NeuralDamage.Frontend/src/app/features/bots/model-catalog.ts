import { computed, inject, Service } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { BotsService } from '../../api/api/bots.service';
import { OpenRouterModel } from '../../core/models';
import { providerIconUrl } from '../../shared/provider-icon';
import { modelDisplayName } from './bot-meta';

/** Providers listed first, in this order; the rest follow alphabetically. */
const LEADING_PROVIDERS = ['DeepSeek', 'OpenAI', 'Google', 'Anthropic', 'Mistral', 'Meta', 'Qwen', 'xAI'];

/** The OpenRouter models a bot may run on: the allowed ones only, loaded once and shared. */
@Service()
export class ModelCatalog {
  private readonly api = inject(BotsService);
  private readonly resource = rxResource({ stream: () => this.api.apiBotsModelsGet() });

  readonly models = computed<readonly OpenRouterModel[]>(() =>
    this.resource.hasValue() ? [...this.resource.value()].sort(byProviderThenName) : [],
  );
  readonly loading = this.resource.isLoading;
  readonly failed = computed(() => !!this.resource.error());

  /** Providers in rail order. */
  readonly providers = computed(() => {
    const seen = new Map<string, string>();
    for (const model of this.models()) {
      if (!seen.has(model.provider)) seen.set(model.provider, providerIconUrl(model));
    }
    return [...seen].map(([name, iconUrl]) => ({ name, iconUrl }));
  });

  find(id: string | null | undefined): OpenRouterModel | undefined {
    return this.models().find((model) => model.id === id);
  }

  /** The model's name without its provider prefix, or the raw id when it is not (or no longer) allowed. */
  nameOf(id: string | null | undefined): string {
    const model = this.find(id);
    return model ? modelDisplayName(model) : (id ?? '');
  }
}

function byProviderThenName(a: OpenRouterModel, b: OpenRouterModel): number {
  const rank = (provider: string) => {
    const index = LEADING_PROVIDERS.indexOf(provider);
    return index === -1 ? LEADING_PROVIDERS.length : index;
  };
  return (
    rank(a.provider) - rank(b.provider) ||
    a.provider.localeCompare(b.provider) ||
    modelDisplayName(a).localeCompare(modelDisplayName(b))
  );
}

/** Case-insensitive match on name, id, provider, description and capabilities ("fast", "code"). */
export function modelMatches(model: OpenRouterModel, query: string): boolean {
  const q = query.trim().toLowerCase();
  if (!q) return true;
  return [model.name, model.id, model.provider, model.description ?? '', ...model.capabilities]
    .join('\n')
    .toLowerCase()
    .includes(q);
}

/**
 * Keeps the catalog order, but puts the models whose name matches the search ahead of those
 * that only match on id, provider, description or capability: "mistral small 3" lists
 * Mistral Small 3 before a model whose blurb compares itself to it.
 */
export function byNameMatchFirst(models: readonly OpenRouterModel[], query: string): OpenRouterModel[] {
  const q = query.trim().toLowerCase();
  if (!q) return [...models];
  const named = (model: OpenRouterModel) =>
    modelDisplayName(model).toLowerCase().includes(q) || model.name.toLowerCase().includes(q) ? 0 : 1;
  return [...models].sort((a, b) => named(a) - named(b));
}
