import { Component, inject, OnInit } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideBot } from '@ng-icons/lucide';
import { HlmEmptyImports } from '@spartan-ng/helm/empty';
import { HlmItemImports } from '@spartan-ng/helm/item';
import { HlmSidebarImports } from '@spartan-ng/helm/sidebar';
import { BotDirectory } from './bot-directory';

/**
 * `/bots`: every bot you can add to a chat. A plain list for now; issue #54 turns this into the
 * bots page with the model browser (see `model-catalog.ts` and prompt-kit's model selector).
 */
@Component({
  selector: 'app-bots-page',
  imports: [NgIcon, HlmEmptyImports, HlmItemImports, HlmSidebarImports],
  providers: [provideIcons({ lucideBot })],
  host: { class: 'flex h-full min-h-0 flex-col' },
  template: `
    <header class="flex h-12 shrink-0 items-center gap-2 border-b px-2 md:px-3">
      <button hlmSidebarTrigger></button>
      <h1 class="text-sm font-semibold">Bots</h1>
    </header>
    <div class="min-h-0 flex-1 overflow-y-auto">
      <div class="mx-auto flex w-full max-w-3xl flex-col gap-2 p-4">
        @for (bot of directory.bots(); track bot.id) {
          <div hlmItem variant="outline">
            <div hlmItemMedia variant="icon"><ng-icon name="lucideBot" /></div>
            <div hlmItemContent>
              <div hlmItemTitle>{{ bot.name }}</div>
              <p hlmItemDescription>{{ bot.modelId }}</p>
            </div>
          </div>
        } @empty {
          <hlm-empty>
            <hlm-empty-header>
              <h2 hlmEmptyTitle>No bots yet</h2>
              <p hlmEmptyDescription>Create one from a chat's People panel.</p>
            </hlm-empty-header>
          </hlm-empty>
        }
      </div>
    </div>
  `,
})
export class BotsPage implements OnInit {
  protected readonly directory = inject(BotDirectory);

  ngOnInit(): void {
    void this.directory.reload();
  }
}
