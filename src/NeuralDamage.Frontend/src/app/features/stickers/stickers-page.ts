import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, ElementRef, inject, OnInit, signal, viewChild } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideEllipsis, lucideFolderUp, lucidePencil, lucideSearch, lucideTrash2, lucideUpload, lucideX } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmAlertDialogImports } from '@spartan-ng/helm/alert-dialog';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import { HlmDropdownMenuImports } from '@spartan-ng/helm/dropdown-menu';
import { HlmEmptyImports } from '@spartan-ng/helm/empty';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmInput } from '@spartan-ng/helm/input';
import { HlmInputGroupImports } from '@spartan-ng/helm/input-group';
import { HlmProgressImports } from '@spartan-ng/helm/progress';
import { HlmSidebarImports } from '@spartan-ng/helm/sidebar';
import { HlmSpinner } from '@spartan-ng/helm/spinner';
import { HlmTextarea } from '@spartan-ng/helm/textarea';
import { AuthService } from '../../core/auth/auth.service';
import { describeApiError } from '../../core/http-errors';
import { StickerDto } from '../../core/models';
import { InView } from '../../shared/in-view';
import { STICKER_TYPES } from './sticker-file';
import { scrollToTopOnSearch, StickerFeed } from './sticker-feed';
import { StickerImage } from './sticker-image';
import { StickerLibrary } from './sticker-library';

/** The longest description the server keeps (StickerRules). */
const MAX_DESCRIPTION = 200;

/**
 * `/stickers`: the shared library everyone sends from, people and bots. Anyone can add stickers;
 * whoever added one can rename it, fix its description or delete it. A sticker without a
 * description works for people but stays hidden from the bots. The grid reads the library a page
 * at a time and searches it on the server; admins can import a whole folder at once.
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
    HlmProgressImports,
    HlmSidebarImports,
    HlmSpinner,
    HlmTextarea,
    StickerImage,
    InView,
  ],
  providers: [StickerFeed, provideIcons({ lucideEllipsis, lucideFolderUp, lucidePencil, lucideSearch, lucideTrash2, lucideUpload, lucideX })],
  host: { class: 'flex h-full min-h-0 flex-col' },
  template: `
    <header class="flex h-12 shrink-0 items-center gap-2 border-b px-2 md:px-3">
      <button hlmSidebarTrigger></button>
      <span class="text-sm font-medium">Stickers</span>
    </header>

    <div #scroller class="min-h-0 flex-1 overflow-y-auto">
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
                [value]="feed.query()"
                (input)="feed.search($any($event.target).value)"
              />
              <div hlmInputGroupAddon><ng-icon name="lucideSearch" /></div>
            </div>
            <input #files type="file" class="hidden" multiple [accept]="types" (change)="add(files)" />
            @if (library.canImport()) {
              <input #folder type="file" class="hidden" webkitdirectory multiple (change)="add(folder)" />
              <button hlmBtn variant="outline" [disabled]="busy()" (click)="folder.click()">
                <ng-icon name="lucideFolderUp" />
                Import folder
              </button>
            }
            <button hlmBtn [disabled]="busy()" (click)="files.click()">
              @if (busy()) {
                <hlm-spinner class="size-4" />
              } @else {
                <ng-icon name="lucideUpload" />
              }
              Add stickers
            </button>
          </div>
        </div>

        @if (library.importing(); as progress) {
          <section class="bg-card flex flex-col gap-2 rounded-xl border p-4" aria-label="Import" data-testid="import-progress">
            <div class="flex items-center gap-2">
              @if (!progress.uploadDone || progress.described < progress.added) {
                <hlm-spinner class="size-4" />
              }
              <span class="flex-1 text-sm font-medium" data-testid="import-summary">
                {{ progress.uploaded }} / {{ progress.total }} uploaded, {{ progress.described }} described
              </span>
              @if (progress.uploadDone) {
                <button hlmBtn variant="ghost" size="icon-sm" aria-label="Hide import" (click)="library.dismissImport()">
                  <ng-icon name="lucideX" />
                </button>
              }
            </div>
            <hlm-progress [value]="progress.total ? (100 * progress.uploaded) / progress.total : 100" aria-label="Uploaded">
              <hlm-progress-indicator />
            </hlm-progress>
            <p class="text-muted-foreground text-xs" data-testid="import-details">
              {{ progress.added }} added · {{ progress.duplicates }} already in the library · {{ progress.failed }} refused
            </p>
            @for (error of progress.errors; track $index) {
              <p class="text-destructive text-xs">{{ error }}</p>
            }
          </section>
        }

        @if (!feed.ready() || (feed.loading() && !feed.items().length)) {
          <div class="flex justify-center py-10"><hlm-spinner /></div>
        } @else if (!feed.items().length) {
          <hlm-empty class="rounded-xl border">
            <hlm-empty-header>
              <h2 hlmEmptyTitle>{{ feed.searched() ? 'No sticker matches' : 'No stickers yet' }}</h2>
              <p hlmEmptyDescription>Add PNG, WebP or GIF images. Animated ones keep moving.</p>
            </hlm-empty-header>
          </hlm-empty>
        } @else {
          <ul class="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-4" aria-label="Stickers">
            @for (sticker of feed.items(); track sticker.id) {
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
          @if (feed.hasMore() && !feed.loading()) {
            <div class="h-1" aria-hidden="true" data-testid="more" [inViewRoot]="scroller" (appInView)="feed.more()"></div>
          } @else if (feed.loading()) {
            <div class="flex justify-center py-4"><hlm-spinner /></div>
          }
        }

        @if (feed.total()) {
          <p class="text-muted-foreground text-[13px]" data-testid="sticker-count">
            @if (feed.searched()) {
              {{ feed.total() }} of {{ library.count() }} stickers match.
            } @else {
              {{ feed.total() }} {{ feed.total() === 1 ? 'sticker' : 'stickers' }}.
            }
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
  protected readonly feed = inject(StickerFeed);
  private readonly auth = inject(AuthService);

  protected readonly types = STICKER_TYPES.join(',');
  protected readonly maxDescription = MAX_DESCRIPTION;
  protected readonly uploading = signal(0);
  protected readonly busy = computed(() => this.uploading() > 0 || !!(this.library.importing() && !this.library.importing()!.uploadDone));
  protected readonly editing = signal<StickerDto | null>(null);
  protected readonly deleting = signal<StickerDto | null>(null);
  protected readonly name = signal('');
  protected readonly description = signal('');
  protected readonly saving = signal(false);

  protected readonly me = computed(() => this.auth.user()?.id ?? null);
  private readonly scroller = viewChild<ElementRef<HTMLElement>>('scroller');

  constructor() {
    scrollToTopOnSearch(this.feed, this.scroller);
  }

  ngOnInit(): void {
    this.feed.pageSize = 60;
    void this.library.refresh();
    void this.feed.reset();
  }

  /** Admins import in bulk, skipping stickers the library has; everyone else uploads one by one. */
  protected async add(input: HTMLInputElement): Promise<void> {
    const files = Array.from(input.files ?? []);
    // Picking the same file again must fire change again.
    input.value = '';
    if (!files.length) return;
    if (this.library.canImport()) {
      await this.library.importFiles(files);
      return;
    }
    await Promise.all(
      files.map(async (file) => {
        this.uploading.update((n) => n + 1);
        try {
          this.feed.prepend(await this.library.upload(file));
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
      this.feed.replace(await this.library.update(sticker.id, this.name().trim().toLowerCase(), this.description().trim()));
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
      this.feed.remove(sticker.id);
    } catch (error) {
      toast.error(describeApiError(error, { fallback: `Could not delete ${sticker.name}.` }));
    }
  }
}
