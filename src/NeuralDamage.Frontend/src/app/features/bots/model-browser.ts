import { Component, computed, ElementRef, inject, input, model, signal, viewChild, afterNextRender, Injector } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideBrain, lucideCheck, lucideCode, lucideEye, lucideSearch, lucideZap } from '@ng-icons/lucide';
import { HlmInputGroupImports } from '@spartan-ng/helm/input-group';
import { HlmSpinner } from '@spartan-ng/helm/spinner';
import { OpenRouterModel } from '../../core/models';
import { capabilityIcons, formatPricing, modelDisplayName, priceTierMarks } from './bot-meta';
import { byNameMatchFirst, ModelCatalog, modelMatches } from './model-catalog';

/** Rail key for every provider at once. */
const ALL = '';

/**
 * The left half of the bot dialog: search, a provider rail and the allowed models, each with its
 * capabilities, a one-line description, a price tier and the exact price. Adapted from the prompt-kit
 * model selector, laid out inline instead of in a popover. Lists every model straight away.
 */
@Component({
  selector: 'app-model-browser',
  imports: [NgIcon, HlmInputGroupImports, HlmSpinner],
  providers: [provideIcons({ lucideBrain, lucideCheck, lucideCode, lucideEye, lucideSearch, lucideZap })],
  host: { class: 'flex min-h-0 min-w-0 flex-col' },
  template: `
    <div class="border-b p-2.5">
      <div hlmInputGroup>
        <input
          #search
          hlmInputGroupInput
          type="search"
          placeholder="Search models, or try fast, code"
          aria-label="Search models"
          [value]="query()"
          (input)="query.set($any($event.target).value)"
        />
        <div hlmInputGroupAddon><ng-icon name="lucideSearch" /></div>
      </div>
    </div>

    <div class="flex min-h-0 flex-1">
      <nav aria-label="Providers" class="hidden w-36 shrink-0 flex-col gap-0.5 overflow-y-auto border-r p-2 sm:flex">
        <button type="button" [class]="railClass(provider() === ALL)" [attr.aria-current]="provider() === ALL || null" (click)="provider.set(ALL)">
          All
        </button>
        @for (p of catalog.providers(); track p.name) {
          <button type="button" [class]="railClass(provider() === p.name)" [attr.aria-current]="provider() === p.name || null" (click)="provider.set(p.name)">
            <img [src]="p.iconUrl" alt="" class="size-4 shrink-0 object-contain dark:invert" />
            <span class="truncate">{{ p.name }}</span>
          </button>
        }
      </nav>

      <div role="listbox" aria-label="Models" class="flex min-w-0 flex-1 flex-col gap-0.5 overflow-y-auto p-2">
        @if (catalog.loading()) {
          <div class="flex flex-1 items-center justify-center"><hlm-spinner /></div>
        } @else {
          @for (m of visible(); track m.id) {
            <button
              type="button"
              role="option"
              [attr.aria-selected]="m.id === selectedId()"
              class="hover:bg-muted focus-visible:ring-ring/50 aria-selected:bg-muted flex w-full items-center gap-2.5 rounded-lg px-2.5 py-2 text-left outline-none focus-visible:ring-2"
              (click)="selectedId.set(m.id)"
            >
              <span class="flex min-w-0 flex-1 flex-col gap-0.5">
                <!-- The name wins the room: it wraps to a second line, the capability icons following it. -->
                <span class="line-clamp-2 text-sm leading-snug font-medium break-words" [attr.title]="nameOf(m)">
                  {{ nameOf(m) }}
                  @for (cap of capabilitiesOf(m); track cap.capability) {
                    <ng-icon [name]="cap.icon" size="14" class="text-muted-foreground ms-1 inline-flex align-[-2px]" [attr.title]="cap.label" [attr.aria-label]="cap.label" role="img" />
                  }
                </span>
                @if (m.description) {
                  <span class="text-muted-foreground truncate text-xs">{{ m.description }}</span>
                }
              </span>
              <span class="flex shrink-0 flex-col items-end gap-0.5">
                <span class="text-xs font-semibold tracking-wider" [attr.aria-label]="'Price tier ' + m.priceTier + ' of 3'">
                  @for (mark of tierOf(m); track $index) {
                    <span [class.text-muted-foreground/40]="!mark.on">$</span>
                  }
                </span>
                <span class="text-muted-foreground text-xs tabular-nums">{{ priceOf(m) }}</span>
              </span>
              <span class="flex w-4 shrink-0 justify-center">
                @if (m.id === selectedId()) {
                  <ng-icon name="lucideCheck" size="16" />
                }
              </span>
            </button>
          } @empty {
            <p class="text-muted-foreground p-4 text-center text-sm">
              {{ catalog.failed() ? 'Could not load the models.' : 'No model matches.' }}
            </p>
          }
        }
      </div>
    </div>

    <div class="text-muted-foreground flex h-9 shrink-0 items-center justify-between border-t px-3 text-xs">
      <span>Price per 1M tokens, input / output</span>
      <span>{{ visible().length }} {{ visible().length === 1 ? 'model' : 'models' }}</span>
    </div>
  `,
})
export class ModelBrowser {
  protected readonly catalog = inject(ModelCatalog);
  private readonly injector = inject(Injector);
  protected readonly ALL = ALL;

  /** The picked model's id, two-way. */
  readonly selectedId = model<string | null>(null);

  protected readonly query = signal('');
  protected readonly provider = signal(ALL);
  private readonly search = viewChild.required<ElementRef<HTMLInputElement>>('search');

  protected readonly visible = computed(() => {
    const provider = this.provider();
    const query = this.query();
    return byNameMatchFirst(
      this.catalog.models().filter((m) => (provider === ALL || m.provider === provider) && modelMatches(m, query)),
      query,
    );
  });

  focusSearch(): void {
    afterNextRender(() => this.search().nativeElement.focus(), { injector: this.injector });
  }

  protected railClass(active: boolean): string {
    return `flex h-8 w-full items-center gap-2 rounded-md px-2 text-left text-sm outline-none focus-visible:ring-2 focus-visible:ring-ring/50 ${
      active ? 'bg-muted font-medium' : 'hover:bg-muted/60'
    }`;
  }

  protected nameOf(m: OpenRouterModel): string {
    return modelDisplayName(m);
  }

  protected capabilitiesOf(m: OpenRouterModel) {
    return capabilityIcons(m.capabilities);
  }

  protected tierOf(m: OpenRouterModel) {
    return priceTierMarks(m.priceTier);
  }

  protected priceOf(m: OpenRouterModel): string {
    return formatPricing(m.pricing) ?? 'Varies';
  }
}
