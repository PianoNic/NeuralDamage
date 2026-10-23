import { Component, inject, output, signal } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideGlobe } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmInputGroupImports } from '@spartan-ng/helm/input-group';
import { HlmSpinner } from '@spartan-ng/helm/spinner';
import { firstValueFrom } from 'rxjs';
import { ImagesService } from '../../api/api/images.service';
import { WebImageResultDto } from '../../api/model/webImageResultDto';
import { toastApiError } from '../../core/http-errors';
import { GeneratedImageDto } from '../../core/models';
import { AvatarImages } from '../../shared/avatar-images';

/** What a person picked from the web: the stored picture, and whether it is for the background. */
export interface WebImagePick {
  image: GeneratedImageDto;
  asBackground: boolean;
}

/**
 * "Search the web" in the layer editor: a query, a grid of results whose thumbnails come through the
 * API (the browser never calls the web), and a pick that has the server fetch the picture and keep
 * it, with the page it came from, as `generated:<id>`.
 */
@Component({
  selector: 'app-web-image-search',
  imports: [NgIcon, HlmButton, HlmInputGroupImports, HlmSpinner],
  providers: [provideIcons({ lucideGlobe })],
  host: { class: 'block' },
  template: `
    <form class="flex flex-col gap-2" (submit)="$event.preventDefault(); search()">
      <label class="text-muted-foreground text-xs" for="web-picture-search">Search the web</label>
      <div hlmInputGroup>
        <input
          id="web-picture-search"
          hlmInputGroupInput
          placeholder="e.g. red panda eating"
          maxlength="120"
          [value]="query()"
          (input)="query.set($any($event.target).value)"
        />
        <div hlmInputGroupAddon align="inline-end">
          <button hlmBtn variant="ghost" size="icon-xs" type="submit" aria-label="Search the web" [disabled]="!query().trim() || searching()">
            <ng-icon name="lucideGlobe" />
          </button>
        </div>
      </div>
      @if (searching()) {
        <hlm-spinner class="size-4" />
      }
      @if (results().length) {
        <div class="grid max-h-80 grid-cols-3 gap-2 overflow-y-auto" data-testid="web-results">
          @for (result of results(); track result.index) {
            <div class="flex flex-col gap-1">
              <button
                type="button"
                class="bg-muted relative aspect-square overflow-hidden rounded-md"
                [attr.aria-label]="'Add ' + (result.title || 'picture')"
                [title]="result.title"
                [disabled]="picking() !== null"
                (click)="pick(result, false)"
              >
                @if (thumbnail(result); as src) {
                  <img [src]="src" alt="" class="size-full object-cover" />
                }
                @if (picking() === result.index) {
                  <span class="bg-background/60 absolute inset-0 flex items-center justify-center"><hlm-spinner class="size-4" /></span>
                }
              </button>
              <button
                type="button"
                class="text-muted-foreground hover:text-foreground text-[11px]"
                [disabled]="picking() !== null"
                (click)="pick(result, true)"
              >
                Background
              </button>
            </div>
          }
        </div>
        <p class="text-muted-foreground text-[11px]">The page a picture comes from is credited with it.</p>
      }
    </form>
  `,
})
export class WebImageSearch {
  private readonly api = inject(ImagesService);
  private readonly images = inject(AvatarImages);

  readonly picked = output<WebImagePick>();

  protected readonly query = signal('');
  protected readonly searching = signal(false);
  protected readonly results = signal<WebImageResultDto[]>([]);
  /** The result being fetched. */
  protected readonly picking = signal<number | null>(null);
  /** The query the results came from, which a pick has to name. */
  private searched = '';

  protected thumbnail(result: WebImageResultDto): string | null {
    return this.images.get(result.thumbnailUrl)();
  }

  async search(): Promise<void> {
    const query = this.query().trim();
    if (!query) return;
    this.searching.set(true);
    try {
      const results = await firstValueFrom(this.api.apiImagesSearchGet(query));
      this.searched = query;
      this.results.set(results);
      if (!results.length) toast.info(`No pictures of "${query}" found.`);
    } catch (error) {
      toastApiError(error);
    } finally {
      this.searching.set(false);
    }
  }

  async pick(result: WebImageResultDto, asBackground: boolean): Promise<void> {
    this.picking.set(result.index);
    try {
      const image = await firstValueFrom(this.api.apiImagesSearchPickPost({ query: this.searched, index: result.index }));
      this.picked.emit({ image, asBackground });
    } catch (error) {
      toastApiError(error, { fallback: 'That picture cannot be used. Pick another one.' });
    } finally {
      this.picking.set(null);
    }
  }
}
