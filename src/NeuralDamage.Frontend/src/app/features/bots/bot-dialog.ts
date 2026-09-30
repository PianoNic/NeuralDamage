import {
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCamera, lucideChevronRight, lucideTriangleAlert, lucideX } from '@ng-icons/lucide';
import { HlmAlertImports } from '@spartan-ng/helm/alert';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmCollapsibleImports } from '@spartan-ng/helm/collapsible';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmInput } from '@spartan-ng/helm/input';
import { HlmRadioGroupImports } from '@spartan-ng/helm/radio-group';
import { HlmSliderImports } from '@spartan-ng/helm/slider';
import { HlmSpinner } from '@spartan-ng/helm/spinner';
import { HlmTextarea } from '@spartan-ng/helm/textarea';
import { firstValueFrom } from 'rxjs';
import { BotsService } from '../../api/api/bots.service';
import { describeApiError } from '../../core/http-errors';
import { BotDto } from '../../core/models';
import {
  hasModelProblem,
  modelDisplayName,
  normalizeAliases,
  priceTierMarks,
  toCreateRequest,
  Visibility,
  visibilityOptions,
} from './bot-meta';
import { ModelBrowser } from './model-browser';
import { ModelCatalog } from './model-catalog';
import { cropAvatar } from '../../shared/crop-avatar';
import { MemberAvatar } from '../../shared/member-avatar';
import { providerIconUrl } from '../../shared/provider-icon';

const DEFAULT_TEMPERATURE = 0.8;

/**
 * New bot / Edit bot: the model browser on the left, the bot on the right. Opened from a chat
 * (`chatId` set) the new bot joins that chat and may be private to it; opened from the Bots page it
 * is always public. A bot's visibility is fixed once it exists.
 */
@Component({
  selector: 'app-bot-dialog',
  imports: [
    NgIcon,
    HlmAlertImports,
    HlmButton,
    HlmCollapsibleImports,
    HlmDialogImports,
    HlmFieldImports,
    HlmInput,
    HlmRadioGroupImports,
    HlmSliderImports,
    HlmSpinner,
    HlmTextarea,
    MemberAvatar,
    ModelBrowser,
  ],
  providers: [provideIcons({ lucideCamera, lucideChevronRight, lucideTriangleAlert, lucideX })],
  template: `
    <hlm-dialog [state]="open() ? 'open' : 'closed'" (closed)="closed.emit()">
      <hlm-dialog-content
        *hlmDialogPortal="let ctx"
        [showCloseButton]="false"
        class="flex h-[min(620px,calc(100dvh-2rem))] w-full max-w-[calc(100%-2rem)] flex-col gap-0 overflow-hidden p-0 sm:max-w-[960px] md:flex-row"
      >
        <app-model-browser
          class="h-56 shrink-0 border-b md:h-auto md:flex-1 md:border-r md:border-b-0"
          [(selectedId)]="modelId"
          aria-label="Choose a model"
        />

        <form
          class="flex min-h-0 w-full flex-1 flex-col gap-4 overflow-y-auto p-5 md:w-[480px] md:flex-none md:shrink-0"
          aria-label="Bot"
          (submit)="$event.preventDefault(); save()"
        >
          <div class="flex items-start gap-2">
            <div class="flex flex-1 flex-col gap-1">
              <h2 hlmDialogTitle>{{ bot() ? 'Edit bot' : 'New bot' }}</h2>
              <p hlmDialogDescription>Describe it like a friend. It decides for itself when to speak.</p>
            </div>
            <button hlmBtn type="button" variant="ghost" size="icon-sm" aria-label="Close" (click)="ctx.close()">
              <ng-icon name="lucideX" />
            </button>
          </div>

          @if (modelWarning(); as warning) {
            <div hlmAlert variant="destructive">
              <ng-icon name="lucideTriangleAlert" />
              <h3 hlmAlertTitle>Pick a new model</h3>
              <p hlmAlertDescription>{{ warning }}</p>
            </div>
          }

          <div class="flex items-center gap-3">
            <button
              type="button"
              class="focus-visible:ring-ring/50 group relative shrink-0 rounded-full outline-none focus-visible:ring-2"
              aria-label="Choose a picture"
              (click)="picker.click()"
            >
              <app-member-avatar [name]="name().trim() || '?'" [avatarUrl]="shownAvatar()" [modelId]="modelId()" [px]="56" />
              <span
                class="bg-foreground/50 text-background absolute inset-0 flex items-center justify-center rounded-full opacity-0 transition-opacity group-hover:opacity-100 group-focus-visible:opacity-100"
              >
                <ng-icon name="lucideCamera" size="18" />
              </span>
            </button>
            <input
              #picker
              type="file"
              class="hidden"
              accept="image/png,image/jpeg,image/webp,image/gif"
              (change)="pickAvatar(picker)"
            />
            <div class="flex min-w-0 flex-col gap-0.5">
              <span class="text-sm font-medium">Picture</span>
              <span class="text-muted-foreground text-xs">
                @if (shownAvatar()) {
                  <button type="button" class="hover:text-foreground underline-offset-2 hover:underline" (click)="picker.click()">
                    Change
                  </button>
                  ·
                  <button type="button" class="hover:text-foreground underline-offset-2 hover:underline" (click)="removeAvatar()">
                    Remove
                  </button>
                } @else {
                  Optional. Without one it shows the model's icon.
                }
              </span>
            </div>
          </div>

          <div class="grid grid-cols-1 gap-3 sm:grid-cols-2">
            <div hlmField [attr.data-invalid]="nameInvalid() || null">
              <label hlmFieldLabel for="bot-name">Name</label>
              <input
                hlmInput
                id="bot-name"
                name="name"
                maxlength="128"
                autocomplete="off"
                [attr.aria-invalid]="nameInvalid() || null"
                [value]="name()"
                (input)="name.set($any($event.target).value)"
                (blur)="nameTouched.set(true)"
              />
              @if (nameInvalid()) {
                <hlm-field-error forceShow>Give it a name.</hlm-field-error>
              }
            </div>
            <div hlmField>
              <label hlmFieldLabel for="bot-aliases">Nicknames</label>
              <input
                hlmInput
                id="bot-aliases"
                name="aliases"
                autocomplete="off"
                placeholder="e.g. mo, mona lisa"
                [value]="aliases()"
                (input)="aliases.set($any($event.target).value)"
              />
            </div>
          </div>

          <div hlmField>
            <label hlmFieldLabel for="bot-personality">Personality</label>
            <textarea
              hlmTextarea
              id="bot-personality"
              name="personality"
              rows="3"
              class="min-h-20 resize-none"
              placeholder="Who is it? How does it talk? What does it care about?"
              [value]="personality()"
              (input)="personality.set($any($event.target).value)"
            ></textarea>
          </div>

          <div hlmField>
            <span hlmFieldLabel id="bot-visibility-label">Visibility</span>
            <hlm-radio-group
              class="grid-cols-2"
              aria-labelledby="bot-visibility-label"
              [value]="visibility()"
              [disabled]="!!bot()"
              (valueChange)="visibility.set($any($event))"
            >
              @for (option of options(); track option) {
                <label
                  [for]="'bot-visibility-' + option"
                  class="has-data-[checked=true]:border-primary has-data-[checked=true]:bg-muted/40 flex cursor-pointer gap-2 rounded-lg border px-2.5 py-2"
                >
                  <hlm-radio [value]="option" [inputId]="'bot-visibility-' + option" class="mt-0.5">
                    <hlm-radio-indicator indicator />
                  </hlm-radio>
                  <span class="flex flex-col gap-0.5">
                    <span class="text-sm font-medium">{{ option === 'public' ? 'Public' : 'Private' }}</span>
                    <span class="text-muted-foreground text-xs">
                      {{ option === 'public' ? 'Anyone can add it.' : 'Only this chat.' }}
                    </span>
                  </span>
                </label>
              }
            </hlm-radio-group>
          </div>

          <div hlmField>
            <span hlmFieldLabel>Model</span>
            <div class="flex items-center gap-2.5 rounded-lg border px-2.5 py-2" aria-live="polite">
              @if (selectedModel(); as m) {
                <img [src]="iconOf(m.id)" alt="" class="size-5 shrink-0 object-contain dark:invert" />
                <span class="flex min-w-0 flex-1 flex-col">
                  <span class="truncate text-sm font-medium">{{ nameOf(m) }}</span>
                  <span class="text-muted-foreground text-xs">Picked on the left</span>
                </span>
                <span class="text-xs font-semibold tracking-wider">
                  @for (mark of tierOf(m.priceTier); track $index) {
                    <span [class.text-muted-foreground/40]="!mark.on">$</span>
                  }
                </span>
              } @else if (modelId()) {
                <span class="flex min-w-0 flex-1 flex-col">
                  <span class="truncate text-sm font-medium">{{ modelId() }}</span>
                  <span class="text-destructive text-xs">Not available any more. Pick another on the left.</span>
                </span>
              } @else {
                <span class="text-muted-foreground text-sm">Pick a model on the left.</span>
              }
            </div>
          </div>

          <div hlmField>
            <span hlmFieldLabel id="bot-temperature-label">Temperature</span>
            <div class="flex items-center gap-3">
              <hlm-slider
                class="flex-1"
                aria-labelledby="bot-temperature-label"
                [min]="0"
                [max]="2"
                [step]="0.1"
                [value]="[temperature()]"
                (valueChange)="temperature.set($event[0])"
              />
              <span class="w-8 text-right text-sm tabular-nums">{{ temperature().toFixed(1) }}</span>
            </div>
          </div>

          <hlm-collapsible [expanded]="advancedOpen()" (expandedChange)="advancedOpen.set($event)" class="flex flex-col gap-2">
            <button
              hlmCollapsibleTrigger
              type="button"
              class="text-muted-foreground hover:text-foreground focus-visible:ring-ring/50 -mx-1 flex w-fit items-center gap-1 rounded-md px-1 text-sm font-medium outline-none focus-visible:ring-2"
            >
              <ng-icon name="lucideChevronRight" size="16" class="transition-transform" [class.rotate-90]="advancedOpen()" />
              Advanced
            </button>
            <hlm-collapsible-content>
              <div hlmField>
                <label hlmFieldLabel for="bot-system-prompt">System prompt</label>
                <textarea
                  hlmTextarea
                  id="bot-system-prompt"
                  name="systemPrompt"
                  rows="4"
                  class="min-h-24 resize-y"
                  [placeholder]="'You are ' + (name().trim() || '<name>') + '.'"
                  [value]="systemPrompt()"
                  (input)="systemPrompt.set($any($event.target).value)"
                ></textarea>
                <p hlmFieldDescription>
                  Replaces the opening line of the bot's instructions. Leave it empty to use the default; the
                  personality above is always included.
                </p>
              </div>
            </hlm-collapsible-content>
          </hlm-collapsible>

          @if (error(); as message) {
            <div hlmAlert variant="destructive" role="alert">
              <ng-icon name="lucideTriangleAlert" />
              <p hlmAlertDescription>{{ message }}</p>
            </div>
          }

          <div class="mt-auto flex justify-end gap-2 pt-1">
            <button hlmBtn type="button" variant="outline" (click)="ctx.close()">Cancel</button>
            <button hlmBtn type="submit" [disabled]="saving() || !modelId()">
              @if (saving()) {
                <hlm-spinner />
              }
              {{ bot() ? 'Save' : 'Create bot' }}
            </button>
          </div>
        </form>
      </hlm-dialog-content>
    </hlm-dialog>
  `,
})
export class BotDialog {
  private readonly api = inject(BotsService);
  private readonly catalog = inject(ModelCatalog);

  readonly open = input(false);
  /** The bot to edit; null makes a new one. */
  readonly bot = input<BotDto | null>(null);
  /** The chat it is opened from, if any. */
  readonly chatId = input<string | null>(null);
  /** Opened to fix a broken model: explain why and put the cursor in the model search. */
  readonly focusModel = input(false);

  readonly closed = output();
  /** The new bot after a create, the edited one's id after an edit. */
  readonly saved = output<{ bot: BotDto | null; id: string }>();

  private readonly browser = viewChild(ModelBrowser);

  protected readonly name = signal('');
  protected readonly nameTouched = signal(false);
  protected readonly aliases = signal('');
  protected readonly personality = signal('');
  protected readonly visibility = signal<Visibility>('public');
  protected readonly modelId = signal<string | null>(null);
  protected readonly temperature = signal(DEFAULT_TEMPERATURE);
  protected readonly systemPrompt = signal('');
  protected readonly advancedOpen = signal(false);
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  /** A picture picked but not uploaded yet: it goes up when the bot is saved. */
  private readonly newAvatar = signal<{ blob: Blob; preview: string } | null>(null);
  /** The bot's current picture is to go when the bot is saved. */
  private readonly avatarRemoved = signal(false);
  protected readonly shownAvatar = computed(
    () => this.newAvatar()?.preview ?? (this.avatarRemoved() ? null : (this.bot()?.avatarUrl ?? null)),
  );

  protected readonly nameInvalid = computed(() => this.nameTouched() && !this.name().trim());
  protected readonly selectedModel = computed(() => this.catalog.find(this.modelId()));
  /** Editing a bot shows its own visibility, fixed; a new one offers what the context allows. */
  protected readonly options = computed<Visibility[]>(() => {
    const bot = this.bot();
    if (bot) return [bot.isPublic ? 'public' : 'private'];
    return visibilityOptions(this.chatId());
  });
  protected readonly modelWarning = computed(() => {
    const bot = this.bot();
    if (!bot || !hasModelProblem(bot.modelStatus) || this.modelId() !== bot.modelId) return null;
    return bot.modelStatusReason ?? `${bot.modelId} can no longer be used.`;
  });

  constructor() {
    // Fill the form each time the dialog opens.
    effect(() => {
      if (!this.open()) return;
      const bot = this.bot();
      untracked(() => {
        this.name.set(bot?.name ?? '');
        this.aliases.set(bot?.aliases ?? '');
        this.personality.set(bot?.personality ?? '');
        this.visibility.set(bot && !bot.isPublic ? 'private' : 'public');
        this.modelId.set(bot?.modelId ?? null);
        this.temperature.set(bot?.temperature ?? DEFAULT_TEMPERATURE);
        this.systemPrompt.set(bot?.systemPrompt ?? '');
        // A bot that already has its own prompt shows it; otherwise the section stays tucked away.
        this.advancedOpen.set(!!bot?.systemPrompt?.trim());
        this.nameTouched.set(false);
        this.error.set(null);
        this.saving.set(false);
        this.setNewAvatar(null);
        this.avatarRemoved.set(false);
      });
    });
    inject(DestroyRef).onDestroy(() => this.setNewAvatar(null));
    effect(() => {
      const browser = this.browser();
      if (browser && this.open() && untracked(() => this.focusModel())) browser.focusSearch();
    });
  }

  protected nameOf(model: { name: string }): string {
    return modelDisplayName(model);
  }

  protected iconOf(id: string): string {
    return providerIconUrl({ id });
  }

  protected tierOf(tier: number) {
    return priceTierMarks(tier);
  }

  protected async pickAvatar(input: HTMLInputElement): Promise<void> {
    const file = input.files?.[0];
    // Cleared so picking the same file again still fires a change.
    input.value = '';
    if (!file) return;
    this.error.set(null);
    try {
      const blob = await cropAvatar(file);
      this.setNewAvatar({ blob, preview: URL.createObjectURL(blob) });
      this.avatarRemoved.set(false);
    } catch {
      this.error.set('That picture could not be read. Try a PNG, JPEG, WebP or GIF.');
    }
  }

  protected removeAvatar(): void {
    this.setNewAvatar(null);
    this.avatarRemoved.set(!!this.bot()?.avatarUrl);
  }

  private setNewAvatar(next: { blob: Blob; preview: string } | null): void {
    const previous = this.newAvatar();
    if (previous) URL.revokeObjectURL(previous.preview);
    this.newAvatar.set(next);
  }

  /** Uploads or removes the picture once the bot exists. Returns the bot's avatar URL after. */
  private async saveAvatar(botId: string, current: string | null | undefined): Promise<string | null | undefined> {
    const picked = this.newAvatar();
    if (picked) {
      const result = await firstValueFrom(this.api.apiBotsBotIdAvatarPut(botId, picked.blob));
      return result.avatarUrl;
    }
    if (this.avatarRemoved() && current) {
      await firstValueFrom(this.api.apiBotsBotIdAvatarDelete(botId));
      return null;
    }
    return current;
  }

  protected async save(): Promise<void> {
    this.nameTouched.set(true);
    const modelId = this.modelId();
    if (!this.name().trim() || !modelId || this.saving()) return;

    this.saving.set(true);
    this.error.set(null);
    try {
      const bot = this.bot();
      if (bot) {
        await firstValueFrom(
          this.api.apiBotsBotIdPut(bot.id, {
            name: this.name().trim(),
            modelId,
            personality: this.personality().trim(),
            aliases: normalizeAliases(this.aliases()),
            temperature: this.temperature(),
            // Always sent, so clearing the field goes back to the server default.
            systemPrompt: this.systemPrompt().trim(),
          }),
        );
        await this.saveAvatar(bot.id, bot.avatarUrl);
        this.saved.emit({ bot: null, id: bot.id });
      } else {
        const created = await firstValueFrom(
          this.api.apiBotsPost(
            toCreateRequest(
              {
                name: this.name(),
                aliases: this.aliases(),
                personality: this.personality(),
                visibility: this.visibility(),
                modelId,
                temperature: this.temperature(),
                systemPrompt: this.systemPrompt(),
              },
              this.chatId(),
            ),
          ),
        );
        // The bot exists now, so a failed upload must not read as a failed create:
        // it is saved without a picture, which can be added by editing it.
        let avatarUrl = created.avatarUrl;
        try {
          avatarUrl = await this.saveAvatar(created.id, created.avatarUrl);
        } catch {
          // Kept quiet on purpose; the bot shows its model's icon.
        }
        this.saved.emit({ bot: { ...created, avatarUrl }, id: created.id });
      }
      this.closed.emit();
    } catch (error) {
      this.error.set(describeApiError(error, { fallback: 'Could not save the bot.' }));
    } finally {
      this.saving.set(false);
    }
  }
}
