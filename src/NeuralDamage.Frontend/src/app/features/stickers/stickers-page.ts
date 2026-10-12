import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideEllipsis, lucidePencil, lucideSearch, lucideTrash2, lucideUpload } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmAlertDialogImports } from '@spartan-ng/helm/alert-dialog';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import { HlmDropdownMenuImports } from '@spartan-ng/helm/dropdown-menu';
import { HlmEmptyImports } from '@spartan-ng/helm/empty';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmInput } from '@spartan-ng/helm/input';
import { HlmInputGroupImports } from '@spartan-ng/helm/input-group';
import { HlmSidebarImports } from '@spartan-ng/helm/sidebar';
import { HlmSpinner } from '@spartan-ng/helm/spinner';
import { HlmTextarea } from '@spartan-ng/helm/textarea';
import { AuthService } from '../../core/auth/auth.service';
import { describeApiError } from '../../core/http-errors';
import { StickerDto } from '../../core/models';
import { STICKER_TYPES } from './sticker-file';
import { StickerImage } from './sticker-image';
import { matchesSticker, StickerLibrary } from './sticker-library';

/** The longest description the server keeps (StickerRules). */
const MAX_DESCRIPTION = 200;

/**
 * `/stickers`: the shared library everyone sends from, people and bots. Anyone can add stickers;
 * whoever added one can rename it, fix its description or delete it. A sticker without a
 * description works for people but stays hidden from the bots.
 */
@Component({
  selector: 'app-stickers-page',
  imports: [
    NgIcon,
    HlmAlertDialogImports,
    HlmButton,
    HlmDialogImports,
    HlmDropdownMenuImports,
    HlmEmptyImports,
    HlmFieldImports,
    HlmInput,
    HlmInputGroupImports,
    HlmSidebarImports,
    HlmSpinner,
    HlmTextarea,
    StickerImage,
  ],
  providers: [provideIcons({ lucideEllipsis, lucidePencil, lucideSearch, lucideTrash2, lucideUpload })],
  host: { class: 'flex h-full min-h-0 flex-col' },
  template: `
    <header class="flex h-12 shrink-0 items-center gap-2 border-b px-2 md:px-3">
      <button hlmSidebarTrigger></button>
      <span class="text-sm font-medium">Stickers</span>
    </header>

    <div class="min-h-0 flex-1 overflow-y-auto">
      <div class="mx-auto flex w-full max-w-5xl flex-col gap-5 px-4 py-6 md:px-8">
        <div class="flex flex-col gap-3 md:flex-row md:items-end">
          <div class="flex flex-1 flex-col gap-1">
            <h1 class="text-2xl font-semibold tracking-tight">Stickers</h1>
            <p class="text-muted-foreground text-sm">
              One library for everyone. People and bots can send any of them from the chat.
            </p>
          </div>
          <div class="flex items-center gap-2">
            <div hlmInputGroup class="md:w-60">
              <input
                hlmInputGroupInput
                type="search"
                placeholder="Search stickers"
                aria-label="Search stickers"
                [value]="query()"
                (input)="query.set($any($event.target).value)"
              />
              <div hlmInputGroupAddon><ng-icon name="lucideSearch" /></div>
            </div>
            <input #files type="file" class="hidden" multiple [accept]="types" (change)="add(files)" />
            <button hlmBtn [disabled]="uploading() > 0" (click)="files.click()">
              @if (uploading() > 0) {
                <hlm-spinner class="size-4" />
              } @else {
                <ng-icon name="lucideUpload" />
              }
              Add stickers
            </button>
          </div>
        </div>

        @if (library.loading() && !library.stickers().length) {
          <div class="flex justify-center py-10"><hlm-spinner /></div>
        } @else if (!visible().length) {
          <hlm-empty class="rounded-xl border">
            <hlm-empty-header>
              <h2 hlmEmptyTitle>{{ query().trim() ? 'No sticker matches' : 'No stickers yet' }}</h2>
              <p hlmEmptyDescription>Add PNG, WebP or GIF images. Animated ones keep moving.</p>
            </hlm-empty-header>
          </hlm-empty>
        } @else {
          <ul class="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-4" aria-label="Stickers">
            @for (sticker of visible(); track sticker.id) {
              <li class="bg-card flex flex-col gap-2 rounded-xl border p-3" data-testid="sticker-card">
                <div class="flex items-start justify-between gap-1">
                  <span class="bg-muted/40 flex flex-1 justify-center rounded-lg py-2">
                    <app-sticker-image [url]="sticker.url" [alt]="sticker.description || sticker.name" [px]="112" />
                  </span>
                  @if (sticker.canEdit) {
                    <button
                      hlmBtn
                      variant="ghost"
                      size="icon-sm"
                      class="-me-1 -mt-1"
                      [attr.aria-label]="'Actions for ' + sticker.name"
                      [hlmDropdownMenuTrigger]="menu"
                      [hlmDropdownMenuTriggerData]="{ $implicit: sticker }"
                      align="end"
                    >
                      <ng-icon name="lucideEllipsis" />
                    </button>
                  }
                </div>
                <div class="flex min-w-0 flex-col gap-0.5">
                  <span class="truncate font-mono text-xs font-medium">{{ sticker.name }}</span>
                  @if (sticker.describing) {
                    <span class="text-muted-foreground flex items-center gap-1.5 text-xs" data-testid="describing">
                      <hlm-spinner class="size-3" /> Describing…
                    </span>
                  } @else if (sticker.description) {
                    <span class="text-muted-foreground line-clamp-3 text-xs">{{ sticker.description }}</span>
                  } @else {
                    <span class="text-muted-foreground text-xs italic" data-testid="undescribed">
                      No description, so bots don't know it yet.
                      @if (sticker.canEdit) {
                        <button type="button" class="text-foreground not-italic underline underline-offset-2" (click)="openEdit(sticker)">
                          Write one
                        </button>
                      }
                    </span>
                  }
                  <span class="text-muted-foreground mt-1 text-[11px]">
                    {{ sticker.uploaderUserId === me() ? 'You' : sticker.uploaderName }} ·
                    {{ sticker.useCount }} {{ sticker.useCount === 1 ? 'send' : 'sends' }}
                  </span>
                </div>
              </li>
            }
          </ul>
        }

        @if (library.stickers().length) {
          <p class="text-muted-foreground text-[13px]">
            {{ library.stickers().length }} {{ library.stickers().length === 1 ? 'sticker' : 'stickers' }}.
            Bots know the ones with a description.
          </p>
        }
      </div>
    </div>

    <ng-template #menu let-sticker>
      <hlm-dropdown-menu class="w-44">
        <button hlmDropdownMenuItem (triggered)="openEdit(sticker)">
          <ng-icon name="lucidePencil" />
          Edit
        </button>
        <button hlmDropdownMenuItem variant="destructive" (triggered)="deleting.set(sticker)">
          <ng-icon name="lucideTrash2" />
          Delete
        </button>
      </hlm-dropdown-menu>
    </ng-template>

    <hlm-dialog [state]="editing() ? 'open' : 'closed'" (closed)="editing.set(null)">
      <hlm-dialog-content *hlmDialogPortal="let ctx" class="sm:max-w-md">
        <form class="flex flex-col gap-4" aria-label="Sticker" (submit)="$event.preventDefault(); save(ctx)">
          <hlm-dialog-header>
            <h2 hlmDialogTitle>Edit sticker</h2>
            <p hlmDialogDescription>Bots pick stickers by their description, so say what it shows and when it fits.</p>
          </hlm-dialog-header>
          @if (editing(); as sticker) {
            <div class="flex justify-center">
              <app-sticker-image [url]="sticker.url" [alt]="sticker.description || sticker.name" [px]="120" />
            </div>
          }
          <div hlmField>
            <label hlmFieldLabel for="sticker-name">Name</label>
            <input
              hlmInput
              id="sticker-name"
              autocomplete="off"
              maxlength="32"
              [value]="name()"
              (input)="name.set($any($event.target).value)"
            />
            <p hlmFieldDescription>Lowercase words with hyphens. Bots send it as [sticker: {{ name() || 'name' }}].</p>
          </div>
          <div hlmField>
            <label hlmFieldLabel for="sticker-description">Description</label>
            <textarea
              hlmTextarea
              id="sticker-description"
              rows="3"
              [attr.maxlength]="maxDescription"
              placeholder="cat crying with laughter, for when something is hilarious"
              [value]="description()"
              (input)="description.set($any($event.target).value)"
            ></textarea>
            <p hlmFieldDescription>Empty hides it from the bots. {{ description().length }} / {{ maxDescription }}</p>
          </div>
          <hlm-dialog-footer>
            <button hlmBtn type="button" variant="outline" (click)="ctx.close()">Cancel</button>
            <button hlmBtn type="submit" [disabled]="saving() || !name().trim()">Save</button>
          </hlm-dialog-footer>
        </form>
      </hlm-dialog-content>
    </hlm-dialog>

    <hlm-alert-dialog [state]="deleting() ? 'open' : 'closed'" (closed)="deleting.set(null)">
      <hlm-alert-dialog-content *hlmAlertDialogPortal="let ctx">
        <hlm-alert-dialog-header>
          <h2 hlmAlertDialogTitle>Delete {{ deleting()?.name }}?</h2>
          <p hlmAlertDialogDescription>
            Nobody can send it any more. Where it was already sent, it stays.
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
export class StickersPage implements OnInit {
  protected readonly library = inject(StickerLibrary);
  private readonly auth = inject(AuthService);

  protected readonly types = STICKER_TYPES.join(',');
  protected readonly maxDescription = MAX_DESCRIPTION;
  protected readonly query = signal('');
  protected readonly uploading = signal(0);
  protected readonly editing = signal<StickerDto | null>(null);
  protected readonly deleting = signal<StickerDto | null>(null);
  protected readonly name = signal('');
  protected readonly description = signal('');
  protected readonly saving = signal(false);

  protected readonly visible = computed(() => {
    const query = this.query();
    return this.library.stickers().filter((s) => matchesSticker(s, query));
  });

  protected readonly me = computed(() => this.auth.user()?.id ?? null);

  ngOnInit(): void {
    void this.library.reload();
  }

  protected async add(input: HTMLInputElement): Promise<void> {
    const files = Array.from(input.files ?? []);
    // Picking the same file again must fire change again.
    input.value = '';
    await Promise.all(
      files.map(async (file) => {
        this.uploading.update((n) => n + 1);
        try {
          await this.library.upload(file);
        } catch (error) {
          toast.error(
            error instanceof HttpErrorResponse || !(error instanceof Error)
              ? describeApiError(error, { fallback: `Could not add ${file.name}.` })
              : error.message,
          );
        } finally {
          this.uploading.update((n) => n - 1);
        }
      }),
    );
  }

  protected openEdit(sticker: StickerDto): void {
    this.name.set(sticker.name);
    this.description.set(sticker.description ?? '');
    this.editing.set(sticker);
  }

  protected async save(ctx: { close: () => void }): Promise<void> {
    const sticker = this.editing();
    if (!sticker) return;
    this.saving.set(true);
    try {
      await this.library.update(sticker.id, this.name().trim().toLowerCase(), this.description().trim());
      ctx.close();
    } catch (error) {
      toast.error(describeApiError(error, { fallback: 'Could not save the sticker.' }));
    } finally {
      this.saving.set(false);
    }
  }

  protected async remove(): Promise<void> {
    const sticker = this.deleting();
    if (!sticker) return;
    try {
      await this.library.remove(sticker.id);
    } catch (error) {
      toast.error(describeApiError(error, { fallback: `Could not delete ${sticker.name}.` }));
    }
  }
}
