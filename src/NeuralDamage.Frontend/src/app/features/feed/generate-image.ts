import { Component, computed, DestroyRef, inject, Injectable, input, output, signal } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideImage, lucideSparkles, lucideX } from '@ng-icons/lucide';
import { PkAuthImage } from '@prompt-kit/auth-image';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmLabel } from '@spartan-ng/helm/label';
import { HlmNativeSelectImports } from '@spartan-ng/helm/native-select';
import { HlmPopoverImports } from '@spartan-ng/helm/popover';
import { HlmSpinner } from '@spartan-ng/helm/spinner';
import { HlmTextarea } from '@spartan-ng/helm/textarea';
import { HlmTooltipImports } from '@spartan-ng/helm/tooltip';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import { ImagesService } from '../../api/api/images.service';
import { toastApiError } from '../../core/http-errors';
import { BotDto, GeneratedImageDto } from '../../core/models';

/** How often a picture being drawn is asked after. */
export const POLL_MS = 2000;

/** The longest description the server takes. */
export const MAX_PROMPT = 1000;

/** What the person asked for. */
export interface ImageRequest {
  prompt: string;
  faceBotId: string | null;
  /** A story's 9:16 background rather than a square picture for a post. */
  forStory?: boolean;
}

/**
 * The picture a person is generating for what they are writing: asks for it, polls it while the
 * generator draws, and holds it until it is posted or discarded. One per composer.
 */
@Injectable()
export class ImageDraft {
  private readonly api = inject(ImagesService);

  readonly image = signal<GeneratedImageDto | null>(null);
  readonly pending = computed(() => this.image()?.status === 'Pending');
  readonly ready = computed(() => this.image()?.status === 'Ready');
  private timer: ReturnType<typeof setTimeout> | null = null;

  constructor() {
    inject(DestroyRef).onDestroy(() => this.stop());
  }

  async generate(request: ImageRequest): Promise<void> {
    const previous = this.image();
    try {
      const image = await firstValueFrom(this.api.apiImagesPost({ prompt: request.prompt, faceBotId: request.faceBotId, forStory: request.forStory ?? false }));
      if (previous) void this.discardOnServer(previous.id);
      this.image.set(image);
      this.schedule(image.id);
    } catch (error) {
      toastApiError(error, { fallback: 'Could not start the picture.' });
    }
  }

  /** The ready picture's id for the post being sent; it then belongs to the post. */
  take(): string | null {
    const image = this.image();
    if (image?.status !== 'Ready') return null;
    this.stop();
    this.image.set(null);
    return image.id;
  }

  /** Puts a picture back after its post failed. */
  restore(image: GeneratedImageDto): void {
    if (!this.image()) this.image.set(image);
  }

  async discard(): Promise<void> {
    const image = this.image();
    this.stop();
    this.image.set(null);
    if (image) await this.discardOnServer(image.id);
  }

  private async discardOnServer(id: string): Promise<void> {
    // A picture left behind is swept after a day anyway.
    await firstValueFrom(this.api.apiImagesImageIdDelete(id)).catch(() => undefined);
  }

  private schedule(id: string): void {
    this.stop();
    if (this.image()?.status !== 'Pending') return;
    this.timer = setTimeout(() => void this.poll(id), POLL_MS);
  }

  private async poll(id: string): Promise<void> {
    this.timer = null;
    try {
      const image = await firstValueFrom(this.api.apiImagesImageIdGet(id));
      if (this.image()?.id !== id) return;
      if (image.status === 'Failed') {
        this.image.set(null);
        toast.error('The picture did not work out.', { description: image.error ?? undefined });
        return;
      }
      this.image.set(image);
    } catch {
      // Asked again on the next round.
    }
    if (this.image()?.id === id) this.schedule(id);
  }

  private stop(): void {
    if (this.timer) clearTimeout(this.timer);
    this.timer = null;
  }
}

/** A generated picture's file on this app's API, which the auth interceptor sends the token to. */
export function generatedImageSrc(url: string): string {
  return `${environment.apiBaseUrl}${url}`;
}

/**
 * The composer's "Generate image" button: a popover with the description and, optionally, a bot
 * whose face goes in the picture (bots with a profile picture only).
 */
@Component({
  selector: 'app-generate-image',
  imports: [NgIcon, HlmButton, HlmLabel, HlmNativeSelectImports, HlmPopoverImports, HlmTextarea, HlmTooltipImports],
  providers: [provideIcons({ lucideSparkles })],
  template: `
    <hlm-popover align="start" [sideOffset]="8">
      @if (label(); as text) {
        <button hlmPopoverTrigger hlmBtn variant="outline" type="button" class="w-full" [disabled]="busy()">
          <ng-icon name="lucideSparkles" />
          {{ text }}
        </button>
      } @else {
        <button
          hlmPopoverTrigger
          hlmBtn
          variant="ghost"
          size="icon-sm"
          type="button"
          aria-label="Generate image"
          hlmTooltip="Generate image"
          [disabled]="busy()"
        >
          <ng-icon name="lucideSparkles" />
        </button>
      }
      <hlm-popover-content *hlmPopoverPortal="let ctx" class="flex w-80 flex-col gap-3 p-3">
        <div class="flex flex-col gap-1.5">
          <label hlmLabel [for]="promptId">Generate image</label>
          <textarea
            hlmTextarea
            [id]="promptId"
            rows="3"
            class="min-h-20 resize-none text-sm"
            placeholder="A cat holding a sign that says &quot;MONDAY&quot;"
            [maxLength]="maxPrompt"
            [value]="prompt()"
            (input)="prompt.set($any($event.target).value)"
            (keydown.enter)="$event.preventDefault(); submit(ctx)"
          ></textarea>
        </div>
        @if (faces().length) {
          <div class="flex flex-col gap-1.5">
            <label hlmLabel [for]="faceId">With the face of</label>
            <hlm-native-select [selectId]="faceId" size="sm" [value]="face()" (valueChange)="face.set($event ?? '')">
              <option hlmNativeSelectOption value="">Nobody</option>
              @for (bot of faces(); track bot.id) {
                <option hlmNativeSelectOption [value]="bot.id">{{ bot.name }}</option>
              }
            </hlm-native-select>
          </div>
        }
        <p class="text-muted-foreground text-xs">
          {{ face() ? 'With a face it takes a minute or two.' : 'It takes a few seconds, longer when others are waiting.' }}
        </p>
        <button hlmBtn size="sm" type="button" [disabled]="!prompt().trim()" (click)="submit(ctx)">
          <ng-icon name="lucideSparkles" />
          Generate
        </button>
      </hlm-popover-content>
    </hlm-popover>
  `,
})
export class GenerateImage {
  /** Every bot the reader knows; only those with a picture can lend their face. */
  readonly bots = input<readonly BotDto[]>([]);
  /** A picture is already being drawn. */
  readonly busy = input(false);
  /** A labelled button instead of the composer's icon. */
  readonly label = input<string | null>(null);
  /** For a story's background: 9:16. */
  readonly forStory = input(false);
  readonly generate = output<ImageRequest>();

  private static nextId = 0;
  protected readonly promptId = `generate-image-${GenerateImage.nextId++}`;
  protected readonly faceId = `${this.promptId}-face`;
  protected readonly maxPrompt = MAX_PROMPT;
  protected readonly prompt = signal('');
  protected readonly face = signal('');
  protected readonly faces = computed(() =>
    this.bots()
      .filter((b) => !!b.avatarUrl)
      .sort((a, b) => a.name.localeCompare(b.name)),
  );

  protected submit(ctx: { close: () => void }): void {
    const prompt = this.prompt().trim();
    if (!prompt) return;
    this.generate.emit({ prompt, faceBotId: this.face() || null, forStory: this.forStory() });
    this.prompt.set('');
    this.face.set('');
    ctx.close();
  }
}

/** The picture in the composer: a placeholder while it is drawn, then the picture, either way with a discard button. */
@Component({
  selector: 'app-generated-image-tile',
  imports: [NgIcon, PkAuthImage, HlmButton, HlmSpinner],
  providers: [provideIcons({ lucideImage, lucideX })],
  host: { class: 'block' },
  template: `
    <div class="relative w-fit pt-1" data-testid="generated-image">
      @if (image().status === 'Ready' && image().url) {
        <pk-auth-image
          [url]="src()"
          [alt]="image().prompt"
          class="bg-muted block size-28 overflow-hidden rounded-lg border"
          imgClass="size-full object-cover"
        />
      } @else {
        <div
          class="bg-muted text-muted-foreground flex size-28 flex-col items-center justify-center gap-2 rounded-lg border px-2 text-center text-xs"
          role="status"
        >
          <hlm-spinner />
          <span>{{ waitingText() }}</span>
        </div>
      }
      <button
        hlmBtn
        variant="secondary"
        size="icon-xs"
        type="button"
        class="absolute -end-1.5 -top-0.5 rounded-full border shadow-xs"
        aria-label="Discard the picture"
        (click)="discard.emit()"
      >
        <ng-icon name="lucideX" />
      </button>
    </div>
  `,
})
export class GeneratedImageTile {
  readonly image = input.required<GeneratedImageDto>();
  readonly discard = output<void>();

  protected readonly src = computed(() => generatedImageSrc(this.image().url ?? ''));
  protected readonly waitingText = computed(() => {
    const ahead = this.image().waiting - 1;
    return ahead > 0 ? `Drawing… ${ahead} ahead` : 'Drawing…';
  });
}
