import { Component, computed, DestroyRef, ElementRef, inject, signal, viewChild } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideArrowDown,
  lucideArrowUp,
  lucideChevronLeft,
  lucideImagePlus,
  lucideSearch,
  lucideSticker,
  lucideTrash2,
  lucideType,
  lucideX,
} from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmInput } from '@spartan-ng/helm/input';
import { HlmInputGroupImports } from '@spartan-ng/helm/input-group';
import { HlmPopoverImports } from '@spartan-ng/helm/popover';
import { HlmSidebarImports } from '@spartan-ng/helm/sidebar';
import { HlmSliderImports } from '@spartan-ng/helm/slider';
import { HlmSpinner } from '@spartan-ng/helm/spinner';
import { HlmTextarea } from '@spartan-ng/helm/textarea';
import { HlmToggleGroupImports } from '@spartan-ng/helm/toggle-group';
import { firstValueFrom } from 'rxjs';
import { BotsService } from '../../api/api/bots.service';
import { BotPictureSuggestionDto } from '../../api/model/botPictureSuggestionDto';
import { describeApiError, toastApiError } from '../../core/http-errors';
import { StickerDto } from '../../core/models';
import { AvatarImages, isApiAvatar } from '../../shared/avatar-images';
import { StickerImage } from '../stickers/sticker-image';
import { StickerPicker } from '../stickers/sticker-picker';
import {
  addImage,
  addSticker,
  addText,
  BACKGROUNDS,
  draftProblem,
  emptyDraft,
  Layer,
  LayerChange,
  layerLabel,
  MAX_IMAGE_LAYERS,
  MAX_TEXT_LENGTH,
  moveLayer,
  PICTURE_SIZE,
  removeLayer,
  reorderLayer,
  STAGE_HEIGHT,
  STAGE_WIDTH,
  StoryDraft,
  TEXT_COLORS,
  TEXT_SIZE,
  toSpec,
  updateLayer,
} from './story-model';
import { StoryStore } from './story-store';

/**
 * `/feed/story`: the story editor. A background colour or picture, text, stickers from the library
 * and pictures (uploaded or found on Wikipedia), dragged into place on a 360 × 640 stage that the
 * server renders three times as large. The spec it builds is the same one bots write.
 */
@Component({
  selector: 'app-story-editor',
  imports: [
    RouterLink,
    NgIcon,
    HlmButton,
    HlmCardImports,
    HlmInput,
    HlmInputGroupImports,
    HlmPopoverImports,
    HlmSidebarImports,
    HlmSliderImports,
    HlmSpinner,
    HlmTextarea,
    HlmToggleGroupImports,
    StickerImage,
    StickerPicker,
  ],
  providers: [
    provideIcons({
      lucideArrowDown,
      lucideArrowUp,
      lucideChevronLeft,
      lucideImagePlus,
      lucideSearch,
      lucideSticker,
      lucideTrash2,
      lucideType,
      lucideX,
    }),
  ],
  host: { class: 'flex h-full min-h-0 flex-col' },
  template: `
    <header class="flex h-12 shrink-0 items-center gap-2 border-b px-2 md:px-3">
      <button hlmSidebarTrigger></button>
      <a routerLink="/feed" class="text-muted-foreground hover:text-foreground flex items-center gap-1 text-sm">
        <ng-icon name="lucideChevronLeft" />
        Feed
      </a>
    </header>

    <div class="min-h-0 flex-1 overflow-y-auto">
      <div class="mx-auto flex w-full max-w-6xl flex-wrap items-start justify-center gap-6 px-4 py-6">
        <!-- What can be added. -->
        <section aria-label="Add" class="flex min-w-[200px] flex-[0_1_230px] flex-col gap-4">
          <h1 class="text-lg font-semibold">New story</h1>

          <div class="flex flex-col gap-2">
            <span class="text-muted-foreground text-xs">Background</span>
            <div class="grid grid-cols-4 gap-2">
              @for (color of backgrounds; track color) {
                <button
                  type="button"
                  class="h-9 rounded-md border"
                  [class.ring-2]="draft().background === color && !draft().backgroundImage"
                  [class.ring-ring]="draft().background === color"
                  [style.background-color]="color"
                  [attr.aria-label]="'Background ' + color"
                  [attr.aria-pressed]="draft().background === color"
                  (click)="setBackground(color)"
                ></button>
              }
            </div>
            <input #bgFile type="file" class="hidden" accept="image/png,image/jpeg,image/webp,image/gif" (change)="onBackgroundFile($event)" />
            <button hlmBtn variant="outline" type="button" [disabled]="uploading()" (click)="bgFile.click()">
              <ng-icon name="lucideImagePlus" />
              Background image
            </button>
            @if (draft().backgroundImage) {
              <button hlmBtn variant="ghost" size="sm" type="button" (click)="clearBackgroundImage()">Remove background image</button>
            }
          </div>

          <button hlmBtn variant="outline" type="button" (click)="apply(addTextLayer)">
            <ng-icon name="lucideType" />
            Add text
          </button>

          <hlm-popover align="start" [sideOffset]="8">
            <button hlmPopoverTrigger hlmBtn variant="outline" type="button" class="w-full">
              <ng-icon name="lucideSticker" />
              Add sticker
            </button>
            <hlm-popover-content *hlmPopoverPortal="let ctx" class="w-auto p-2">
              <app-sticker-picker (pick)="onSticker($event); ctx.close()" />
            </hlm-popover-content>
          </hlm-popover>

          <input #imageFile type="file" class="hidden" accept="image/png,image/jpeg,image/webp,image/gif" (change)="onImageFile($event)" />
          <button hlmBtn variant="outline" type="button" [disabled]="uploading() || imageCount() >= maxImages" (click)="imageFile.click()">
            <ng-icon name="lucideImagePlus" />
            Add image
          </button>

          <form class="flex flex-col gap-2" (submit)="$event.preventDefault(); findPictures()">
            <label class="text-muted-foreground text-xs" for="story-picture-search">Find a picture on Wikipedia</label>
            <div hlmInputGroup>
              <input id="story-picture-search" hlmInputGroupInput placeholder="e.g. Eiffel Tower" [value]="query()" (input)="query.set($any($event.target).value)" />
              <div hlmInputGroupAddon align="inline-end">
                <button hlmBtn variant="ghost" size="icon-xs" type="submit" aria-label="Find pictures" [disabled]="!query().trim() || searching()">
                  <ng-icon name="lucideSearch" />
                </button>
              </div>
            </div>
            @if (searching()) {
              <hlm-spinner class="size-4" />
            }
            @if (found().length) {
              <div class="grid grid-cols-3 gap-2" data-testid="wiki-results">
                @for (picture of found(); track picture.index) {
                  <div class="flex flex-col gap-1">
                    <button type="button" class="bg-muted aspect-square overflow-hidden rounded-md" [attr.aria-label]="'Add ' + picture.title" (click)="addWiki(picture)">
                      @if (thumbnail(picture); as src) {
                        <img [src]="src" alt="" class="size-full object-cover" />
                      }
                    </button>
                    <button type="button" class="text-muted-foreground hover:text-foreground text-[11px]" (click)="useWikiAsBackground(picture)">As background</button>
                  </div>
                }
              </div>
            }
          </form>
        </section>

        <!-- The stage. -->
        <section aria-label="Story canvas" class="flex flex-none flex-col items-center gap-2">
          <div
            #stage
            class="relative touch-none overflow-hidden rounded-2xl border select-none"
            data-testid="stage"
            [style.width.px]="stageWidth"
            [style.height.px]="stageHeight"
            [style.background-color]="draft().background"
            (pointermove)="onPointerMove($event)"
            (pointerup)="dragging = null"
            (pointercancel)="dragging = null"
          >
            @if (draft().backgroundImage; as bg) {
              @if (src(bg.previewUrl); as url) {
                <img [src]="url" alt="" class="pointer-events-none absolute inset-0 size-full object-cover" />
              }
            }
            @for (layer of draft().layers; track layer.id) {
              <div
                class="absolute cursor-grab"
                data-testid="stage-layer"
                [class.outline-2]="layer.id === draft().selectedId"
                [class.outline-dashed]="layer.id === draft().selectedId"
                [class.outline-offset-4]="layer.id === draft().selectedId"
                [class.outline-white]="layer.id === draft().selectedId"
                [style.left.%]="layer.x"
                [style.top.%]="layer.y"
                [style.transform]="'translate(-50%, -50%) rotate(' + rotationOf(layer) + 'deg)'"
                [style.width]="layer.type === 'text' ? '85%' : layer.size + 'px'"
                (pointerdown)="onPointerDown($event, layer)"
              >
                @switch (layer.type) {
                  @case ('text') {
                    <p
                      class="text-center break-words whitespace-pre-wrap"
                      style="font-family: Inter, var(--font-sans); line-height: 1.25"
                      [style.font-size.px]="layer.size"
                      [style.font-weight]="layer.weight"
                      [style.color]="layer.color"
                    >{{ layer.text }}</p>
                  }
                  @case ('sticker') {
                    <app-sticker-image class="pointer-events-none" [url]="layer.url" [alt]="layer.name" [px]="layer.size" />
                  }
                  @case ('image') {
                    @if (src(layer.previewUrl); as url) {
                      <img [src]="url" alt="" class="pointer-events-none w-full" draggable="false" />
                    }
                  }
                }
              </div>
            }
          </div>
          <span class="text-muted-foreground text-xs">Drag layers to move them. Click one to edit it.</span>
        </section>

        <!-- Layers, the selected one, the spec, and posting. -->
        <section aria-label="Layer settings" class="flex max-w-[420px] min-w-[260px] flex-[1_1_300px] flex-col gap-4">
          <div hlmCard class="gap-2 px-3.5" size="sm">
            <h2 class="text-sm font-semibold">Layers</h2>
            @for (layer of draft().layers; track layer.id; let i = $index) {
              <div class="flex items-center gap-1">
                <button
                  type="button"
                  class="hover:bg-accent min-h-9 flex-1 truncate rounded-md border px-2.5 text-left text-[13px]"
                  [class.border-ring]="layer.id === draft().selectedId"
                  [attr.aria-pressed]="layer.id === draft().selectedId"
                  data-testid="layer-row"
                  (click)="select(layer.id)"
                >
                  {{ label(layer) }}
                </button>
                <button hlmBtn variant="ghost" size="icon-xs" type="button" aria-label="Bring forward" [disabled]="i === draft().layers.length - 1" (click)="apply(reorder(layer.id, 1))">
                  <ng-icon name="lucideArrowUp" />
                </button>
                <button hlmBtn variant="ghost" size="icon-xs" type="button" aria-label="Send backward" [disabled]="i === 0" (click)="apply(reorder(layer.id, -1))">
                  <ng-icon name="lucideArrowDown" />
                </button>
              </div>
            } @empty {
              <p class="text-muted-foreground text-[13px]">Nothing yet. Add text, a sticker or a picture.</p>
            }
          </div>

          @if (selected(); as layer) {
            <div hlmCard class="gap-3 px-3.5" size="sm" data-testid="layer-settings">
              <h2 class="text-sm font-semibold">{{ layer.type === 'text' ? 'Text layer' : layer.type === 'sticker' ? 'Sticker: ' + layer.name : 'Picture' }}</h2>
              @if (layer.type === 'text') {
                <label class="text-muted-foreground text-xs" for="layer-text">Text</label>
                <textarea
                  id="layer-text"
                  hlmTextarea
                  rows="3"
                  [attr.maxlength]="maxText"
                  [value]="layer.text"
                  (input)="change(layer.id, { text: $any($event.target).value })"
                ></textarea>
                <div class="flex flex-wrap items-center gap-2">
                  @for (color of textColors; track color) {
                    <button
                      type="button"
                      class="size-8 rounded-full border"
                      [class.ring-2]="layer.color === color"
                      [class.ring-ring]="layer.color === color"
                      [style.background-color]="color"
                      [attr.aria-label]="'Text colour ' + color"
                      (click)="change(layer.id, { color })"
                    ></button>
                  }
                  <input type="color" class="size-8 cursor-pointer rounded-full" aria-label="Another text colour" [value]="layer.color" (input)="change(layer.id, { color: $any($event.target).value })" />
                </div>
                <hlm-toggle-group type="single" variant="outline" size="sm" [nullable]="false" [value]="layer.weight >= 600 ? 'bold' : 'regular'" (valueChange)="change(layer.id, { weight: $event === 'bold' ? 700 : 400 })">
                  <button hlmToggleGroupItem value="regular" class="px-3">Regular</button>
                  <button hlmToggleGroupItem value="bold" class="px-3 font-bold">Bold</button>
                </hlm-toggle-group>
              }
              <span class="text-muted-foreground text-xs" id="layer-size-label">Size {{ layer.size }}</span>
              <hlm-slider
                aria-labelledby="layer-size-label"
                [min]="layer.type === 'text' ? textSize.min : pictureSize.min"
                [max]="layer.type === 'text' ? textSize.max : pictureSize.max"
                [step]="1"
                [value]="[layer.size]"
                (valueChange)="change(layer.id, { size: $event[0] })"
              />
              @if (layer.type !== 'text') {
                <span class="text-muted-foreground text-xs" id="layer-rotation-label">Rotation {{ layer.rotation }}°</span>
                <hlm-slider aria-labelledby="layer-rotation-label" [min]="-180" [max]="180" [step]="1" [value]="[layer.rotation]" (valueChange)="change(layer.id, { rotation: $event[0] })" />
              }
              <button hlmBtn variant="destructive" size="sm" type="button" (click)="apply(remove(layer.id))">
                <ng-icon name="lucideTrash2" />
                Delete layer
              </button>
            </div>
          }

          @if (preview(); as url) {
            <div class="flex items-start gap-3">
              <img [src]="url" alt="Rendered story" class="h-48 w-[108px] rounded-md border" data-testid="preview" />
              <p class="text-muted-foreground text-[13px]">This is the PNG that gets posted, rendered by the server at 1080 × 1920.</p>
            </div>
          }
          @if (problem(); as message) {
            <p class="text-muted-foreground text-[13px]" role="status">{{ message }}</p>
          }
          <div class="flex gap-2">
            <button hlmBtn variant="outline" type="button" class="flex-1" [disabled]="!!problem() || busy()" (click)="renderPreview()">Preview</button>
            <button hlmBtn type="button" class="flex-1" [disabled]="!!problem() || busy()" (click)="post()">
              @if (busy()) {
                <hlm-spinner />
              }
              Post story
            </button>
          </div>
        </section>
      </div>
    </div>
  `,
})
export class StoryEditor {
  private readonly store = inject(StoryStore);
  private readonly bots = inject(BotsService);
  private readonly images = inject(AvatarImages);
  private readonly router = inject(Router);
  private readonly stage = viewChild.required<ElementRef<HTMLElement>>('stage');

  protected readonly stageWidth = STAGE_WIDTH;
  protected readonly stageHeight = STAGE_HEIGHT;
  protected readonly backgrounds = BACKGROUNDS;
  protected readonly textColors = TEXT_COLORS;
  protected readonly textSize = TEXT_SIZE;
  protected readonly pictureSize = PICTURE_SIZE;
  protected readonly maxText = MAX_TEXT_LENGTH;
  protected readonly maxImages = MAX_IMAGE_LAYERS;
  protected readonly label = layerLabel;

  readonly draft = signal<StoryDraft>(addText(emptyDraft(), 'Hello from my story'));
  protected readonly uploading = signal(false);
  protected readonly busy = signal(false);
  protected readonly preview = signal<string | null>(null);
  protected readonly query = signal('');
  protected readonly searching = signal(false);
  protected readonly found = signal<(BotPictureSuggestionDto & { name: string })[]>([]);

  protected readonly selected = computed(() => this.draft().layers.find((l) => l.id === this.draft().selectedId) ?? null);
  protected readonly problem = computed(() => draftProblem(this.draft()));
  protected readonly imageCount = computed(() => this.draft().layers.filter((l) => l.type === 'image').length);

  /** The layer being dragged. */
  protected dragging: string | null = null;
  private readonly objectUrls: string[] = [];

  protected readonly addTextLayer = (d: StoryDraft) => addText(d);
  protected readonly reorder = (id: string, by: 1 | -1) => (d: StoryDraft) => reorderLayer(d, id, by);
  protected readonly remove = (id: string) => (d: StoryDraft) => removeLayer(d, id);

  constructor() {
    inject(DestroyRef).onDestroy(() => {
      this.objectUrls.forEach((u) => URL.revokeObjectURL(u));
      const preview = this.preview();
      if (preview) URL.revokeObjectURL(preview);
    });
  }

  /** Every change goes through here, and an old preview no longer shows the story. */
  apply(change: (draft: StoryDraft) => StoryDraft): void {
    this.draft.update(change);
    this.clearPreview();
  }

  protected change(id: string, change: LayerChange): void {
    this.apply((d) => updateLayer(d, id, change));
  }

  protected select(id: string): void {
    this.draft.update((d) => ({ ...d, selectedId: id }));
  }

  protected setBackground(color: string): void {
    this.apply((d) => ({ ...d, background: color }));
  }

  protected clearBackgroundImage(): void {
    this.apply((d) => ({ ...d, backgroundImage: null }));
  }

  protected rotationOf(layer: Layer): number {
    return layer.type === 'text' ? 0 : layer.rotation;
  }

  /** An object URL as is; an API URL loaded with the token. */
  protected src(url: string): string | null {
    return isApiAvatar(url) ? this.images.get(url)() : url;
  }

  protected thumbnail(picture: BotPictureSuggestionDto & { name: string }): string | null {
    return this.images.get(this.wikiUrl(picture))();
  }

  private wikiUrl(picture: BotPictureSuggestionDto & { name: string }): string {
    return `/api/bots/picture-suggestions/${picture.index}?name=${encodeURIComponent(picture.name)}`;
  }

  private wikiRef(picture: BotPictureSuggestionDto & { name: string }): string {
    return picture.index ? `wiki:${picture.name}#${picture.index}` : `wiki:${picture.name}`;
  }

  protected onSticker(sticker: StickerDto): void {
    this.apply((d) => addSticker(d, sticker.name, sticker.url));
  }

  protected async onBackgroundFile(event: Event): Promise<void> {
    const upload = await this.upload(event);
    if (upload) this.apply((d) => ({ ...d, backgroundImage: upload }));
  }

  protected async onImageFile(event: Event): Promise<void> {
    const upload = await this.upload(event);
    if (upload) this.apply((d) => addImage(d, upload.ref, upload.previewUrl));
  }

  private async upload(event: Event): Promise<{ ref: string; previewUrl: string } | null> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (!file) return null;
    this.uploading.set(true);
    try {
      const uploaded = await this.store.uploadImage(file);
      const previewUrl = URL.createObjectURL(file);
      this.objectUrls.push(previewUrl);
      return { ref: uploaded.ref, previewUrl };
    } catch (error) {
      toastApiError(error, { network: 'Not uploaded. Check your connection and try again.' });
      return null;
    } finally {
      this.uploading.set(false);
    }
  }

  protected async findPictures(): Promise<void> {
    const name = this.query().trim();
    if (!name) return;
    this.searching.set(true);
    try {
      const results = await firstValueFrom(this.bots.apiBotsPictureSuggestionsGet(name));
      this.found.set(results.map((r) => ({ ...r, name })));
      if (!results.length) toast.info(`No picture of "${name}" found.`);
    } catch (error) {
      toastApiError(error);
    } finally {
      this.searching.set(false);
    }
  }

  protected addWiki(picture: BotPictureSuggestionDto & { name: string }): void {
    this.apply((d) => addImage(d, this.wikiRef(picture), this.wikiUrl(picture)));
  }

  protected useWikiAsBackground(picture: BotPictureSuggestionDto & { name: string }): void {
    this.apply((d) => ({ ...d, backgroundImage: { ref: this.wikiRef(picture), previewUrl: this.wikiUrl(picture) } }));
  }

  protected onPointerDown(event: PointerEvent, layer: Layer): void {
    event.preventDefault();
    this.dragging = layer.id;
    this.select(layer.id);
    (event.currentTarget as HTMLElement).setPointerCapture?.(event.pointerId);
  }

  protected onPointerMove(event: PointerEvent): void {
    if (!this.dragging) return;
    const rect = this.stage().nativeElement.getBoundingClientRect();
    const x = ((event.clientX - rect.left) / rect.width) * 100;
    const y = ((event.clientY - rect.top) / rect.height) * 100;
    const id = this.dragging;
    this.apply((d) => moveLayer(d, id, x, y));
  }

  protected async renderPreview(): Promise<void> {
    this.busy.set(true);
    try {
      const png = await this.store.preview(toSpec(this.draft()));
      this.clearPreview();
      this.preview.set(URL.createObjectURL(png));
    } catch (error) {
      toast.error(await this.reason(error));
    } finally {
      this.busy.set(false);
    }
  }

  protected async post(): Promise<void> {
    this.busy.set(true);
    try {
      await this.store.publish(toSpec(this.draft()));
      toast.success('Story posted. It stays up for 24 hours.');
      void this.router.navigate(['/feed']);
    } catch (error) {
      toastApiError(error, { network: 'Not posted. Check your connection and try again.' });
    } finally {
      this.busy.set(false);
    }
  }

  private clearPreview(): void {
    const preview = this.preview();
    if (preview) URL.revokeObjectURL(preview);
    this.preview.set(null);
  }

  /** A refused preview's reason comes back as a Blob, since the call asked for an image. */
  private async reason(error: unknown): Promise<string> {
    const body = (error as { error?: unknown })?.error;
    if (body instanceof Blob) {
      const text = await body.text().catch(() => '');
      if (text.trim()) return text.trim();
    }
    return describeApiError(error);
  }
}
