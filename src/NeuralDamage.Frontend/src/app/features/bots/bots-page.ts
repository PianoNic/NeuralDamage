import { Component, computed, effect, inject, signal } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideEllipsis, lucidePencil, lucidePlus, lucideSearch, lucideTrash2, lucideTriangleAlert } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmAlertDialogImports } from '@spartan-ng/helm/alert-dialog';
import { HlmBadge } from '@spartan-ng/helm/badge';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmDropdownMenuImports } from '@spartan-ng/helm/dropdown-menu';
import { HlmEmptyImports } from '@spartan-ng/helm/empty';
import { HlmInputGroupImports } from '@spartan-ng/helm/input-group';
import { HlmSidebarImports } from '@spartan-ng/helm/sidebar';
import { HlmSpinner } from '@spartan-ng/helm/spinner';
import { HlmTableImports } from '@spartan-ng/helm/table';
import { HlmToggleGroupImports } from '@spartan-ng/helm/toggle-group';
import { HlmTooltipImports } from '@spartan-ng/helm/tooltip';
import { firstValueFrom } from 'rxjs';
import { BotsService } from '../../api/api/bots.service';
import { AuthService } from '../../core/auth/auth.service';
import { describeApiError } from '../../core/http-errors';
import { BotDto } from '../../core/models';
import { MemberAvatar } from '../../shared/member-avatar';
import { BotDialog } from './bot-dialog';
import { BotDirectory } from './bot-directory';
import { formatPricing, hasModelProblem, modelProblemLabel } from './bot-meta';
import { ModelCatalog } from './model-catalog';

type Filter = 'all' | 'mine';

/** `/bots`: the public bots from everyone, with their model, maker and how much they are used. */
@Component({
  selector: 'app-bots-page',
  imports: [
    NgIcon,
    HlmAlertDialogImports,
    HlmBadge,
    HlmButton,
    HlmDropdownMenuImports,
    HlmEmptyImports,
    HlmInputGroupImports,
    HlmSidebarImports,
    HlmSpinner,
    HlmTableImports,
    HlmToggleGroupImports,
    HlmTooltipImports,
    MemberAvatar,
    BotDialog,
  ],
  providers: [provideIcons({ lucideEllipsis, lucidePencil, lucidePlus, lucideSearch, lucideTrash2, lucideTriangleAlert })],
  host: { class: 'flex h-full min-h-0 flex-col' },
  template: `
    <header class="flex h-12 shrink-0 items-center gap-2 border-b px-2 md:px-3">
      <button hlmSidebarTrigger></button>
      <span class="text-sm font-medium">Bots</span>
    </header>

    <div class="min-h-0 flex-1 overflow-y-auto">
      <div class="mx-auto flex w-full max-w-5xl flex-col gap-5 px-4 py-6 md:px-8">
        <div class="flex flex-col gap-3 md:flex-row md:items-end">
          <div class="flex flex-1 flex-col gap-1">
            <h1 class="text-2xl font-semibold tracking-tight">Bots</h1>
            <p class="text-muted-foreground text-sm">
              Public bots from everyone. Add any of them to a chat from its People panel.
            </p>
          </div>
          <div class="flex items-center gap-2">
            <div hlmInputGroup class="md:w-60">
              <input
                hlmInputGroupInput
                type="search"
                placeholder="Search bots"
                aria-label="Search bots"
                [value]="query()"
                (input)="query.set($any($event.target).value)"
              />
              <div hlmInputGroupAddon><ng-icon name="lucideSearch" /></div>
            </div>
            <button hlmBtn (click)="openDialog(null)">
              <ng-icon name="lucidePlus" />
              New bot
            </button>
          </div>
        </div>

        <hlm-toggle-group
          type="single"
          variant="outline"
          aria-label="Which bots"
          [nullable]="false"
          [value]="filter()"
          (valueChange)="filter.set($any($event))"
        >
          <button hlmToggleGroupItem value="all" class="px-4">All</button>
          <button hlmToggleGroupItem value="mine" class="px-4">Created by me</button>
        </hlm-toggle-group>

        @if (loading() && !bots().length) {
          <div class="flex justify-center py-10"><hlm-spinner /></div>
        } @else if (!visible().length) {
          <hlm-empty class="rounded-xl border">
            <hlm-empty-header>
              <h2 hlmEmptyTitle>{{ query().trim() ? 'No bot matches' : filter() === 'mine' ? 'You have no public bots yet' : 'No bots yet' }}</h2>
              <p hlmEmptyDescription>Make one with New bot, then add it to a chat from its People panel.</p>
            </hlm-empty-header>
          </hlm-empty>
        } @else {
          <div hlmTableContainer class="rounded-xl border">
            <table hlmTable class="table-fixed">
              <thead hlmTHead>
                <tr hlmTr>
                  <th hlmTh class="w-[34%] ps-4">Bot</th>
                  <th hlmTh class="w-[24%]">Model</th>
                  <th hlmTh class="hidden w-[16%] md:table-cell">By</th>
                  <th hlmTh class="hidden w-[10%] text-right md:table-cell">In chats</th>
                  <th hlmTh class="hidden w-[11%] text-right sm:table-cell">Replies (7d)</th>
                  <th hlmTh class="w-12"><span class="sr-only">Actions</span></th>
                </tr>
              </thead>
              <tbody hlmTBody>
                @for (bot of visible(); track bot.id) {
                  <tr hlmTr>
                    <td hlmTd class="ps-4">
                      <div class="flex min-w-0 items-center gap-2.5">
                        <app-member-avatar [name]="bot.name" [avatarUrl]="bot.avatarUrl" [modelId]="bot.modelId" [px]="32" />
                        <div class="flex min-w-0 flex-col leading-snug">
                          <span class="truncate text-sm font-medium">{{ bot.name }}</span>
                          @if (bot.personality) {
                            <span class="text-muted-foreground truncate text-[13px]">{{ bot.personality }}</span>
                          }
                        </div>
                      </div>
                    </td>
                    <td hlmTd>
                      <div class="flex min-w-0 flex-col leading-snug">
                        <span class="flex min-w-0 items-center gap-1.5">
                          <span class="truncate text-sm">{{ catalog.nameOf(bot.modelId) }}</span>
                          @if (broken(bot)) {
                            <span
                              hlmBadge
                              variant="destructive"
                              tabindex="0"
                              [hlmTooltip]="bot.modelStatusReason || problemLabel(bot)"
                              [attr.aria-label]="problemLabel(bot) + ': ' + (bot.modelStatusReason || '')"
                            >
                              <ng-icon name="lucideTriangleAlert" />
                              {{ problemLabel(bot) }}
                            </span>
                          }
                        </span>
                        @if (priceOf(bot); as price) {
                          <span class="text-muted-foreground text-xs tabular-nums">{{ price }} per 1M</span>
                        }
                      </div>
                    </td>
                    <td hlmTd class="hidden md:table-cell">
                      <span class="flex min-w-0 items-center gap-2">
                        <app-member-avatar [name]="bot.createdBy.displayName" [px]="22" />
                        <span class="truncate text-sm">{{ mine(bot) ? 'You' : bot.createdBy.displayName }}</span>
                      </span>
                    </td>
                    <td hlmTd class="hidden text-right tabular-nums md:table-cell">{{ bot.chatCount }}</td>
                    <td hlmTd class="hidden text-right tabular-nums sm:table-cell">{{ bot.repliesLast7Days }}</td>
                    <td hlmTd class="pe-2 text-right">
                      @if (mine(bot)) {
                        <button
                          hlmBtn
                          variant="ghost"
                          size="icon-sm"
                          [attr.aria-label]="'Actions for ' + bot.name"
                          [hlmDropdownMenuTrigger]="menu"
                          [hlmDropdownMenuTriggerData]="{ $implicit: bot }"
                          align="end"
                        >
                          <ng-icon name="lucideEllipsis" />
                        </button>
                      }
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }

        <p class="text-muted-foreground text-[13px]">
          {{ bots().length }} public {{ bots().length === 1 ? 'bot' : 'bots' }}. Private bots live only in the chat
          they were made for and are not listed here.
        </p>
      </div>
    </div>

    <ng-template #menu let-bot>
      <hlm-dropdown-menu class="w-44">
        <button hlmDropdownMenuItem (triggered)="openDialog(bot)">
          <ng-icon name="lucidePencil" />
          {{ broken(bot) ? 'Update model' : 'Edit' }}
        </button>
        <button hlmDropdownMenuItem variant="destructive" (triggered)="deleting.set(bot)">
          <ng-icon name="lucideTrash2" />
          Delete
        </button>
      </hlm-dropdown-menu>
    </ng-template>

    <app-bot-dialog
      [open]="dialogOpen()"
      [bot]="editing()"
      [focusModel]="!!editing() && broken(editing()!)"
      (closed)="dialogOpen.set(false)"
      (saved)="load()"
    />

    <hlm-alert-dialog [state]="deleting() ? 'open' : 'closed'" (closed)="deleting.set(null)">
      <hlm-alert-dialog-content *hlmAlertDialogPortal="let ctx">
        <hlm-alert-dialog-header>
          <h2 hlmAlertDialogTitle>Delete {{ deleting()?.name }}?</h2>
          <p hlmAlertDialogDescription>
            It leaves every chat it is in and is gone for good. Messages it already sent stay.
          </p>
        </hlm-alert-dialog-header>
        <hlm-alert-dialog-footer>
          <button hlmAlertDialogCancel (click)="ctx.close()">Cancel</button>
          <button hlmAlertDialogAction variant="destructive" (click)="remove(); ctx.close()">Delete</button>
        </hlm-alert-dialog-footer>
      </hlm-alert-dialog-content>
    </hlm-alert-dialog>
  `,
})
export class BotsPage {
  private readonly api = inject(BotsService);
  private readonly auth = inject(AuthService);
  private readonly directory = inject(BotDirectory);
  protected readonly catalog = inject(ModelCatalog);

  protected readonly filter = signal<Filter>('all');
  protected readonly query = signal('');
  protected readonly bots = signal<BotDto[]>([]);
  protected readonly loading = signal(false);
  protected readonly dialogOpen = signal(false);
  protected readonly editing = signal<BotDto | null>(null);
  protected readonly deleting = signal<BotDto | null>(null);

  protected readonly visible = computed(() => {
    const query = this.query().trim().toLowerCase();
    return this.bots().filter(
      (b) =>
        !query ||
        b.name.toLowerCase().includes(query) ||
        (b.personality ?? '').toLowerCase().includes(query) ||
        (b.aliases ?? '').toLowerCase().includes(query),
    );
  });

  private loadId = 0;

  constructor() {
    effect(() => {
      this.filter();
      void this.load();
    });
  }

  protected async load(): Promise<void> {
    const id = ++this.loadId;
    this.loading.set(true);
    try {
      const bots = await firstValueFrom(this.api.apiBotsGet(this.filter() === 'mine'));
      if (id === this.loadId) this.bots.set(bots);
    } catch (error) {
      if (id === this.loadId) toast.error(describeApiError(error, { fallback: 'Could not load the bots.' }));
    } finally {
      if (id === this.loadId) this.loading.set(false);
    }
    // The sidebar count follows the directory.
    void this.directory.reload();
  }

  protected openDialog(bot: BotDto | null): void {
    this.editing.set(bot);
    this.dialogOpen.set(true);
  }

  protected mine(bot: BotDto): boolean {
    return bot.createdById === this.auth.user()?.id;
  }

  protected broken(bot: BotDto): boolean {
    return hasModelProblem(bot.modelStatus);
  }

  protected problemLabel(bot: BotDto): string {
    return modelProblemLabel(bot.modelStatus);
  }

  protected priceOf(bot: BotDto): string | null {
    return formatPricing(this.catalog.find(bot.modelId)?.pricing);
  }

  protected async remove(): Promise<void> {
    const bot = this.deleting();
    if (!bot) return;
    try {
      await this.directory.remove(bot.id);
      this.bots.update((list) => list.filter((b) => b.id !== bot.id));
    } catch (error) {
      toast.error(describeApiError(error, { fallback: `Could not delete ${bot.name}.` }));
    }
  }
}
