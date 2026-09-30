import { Component, computed, input, signal } from '@angular/core';
import { PkAuthImage } from '@prompt-kit/auth-image';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import { environment } from '../../../environments/environment';
import type { AttachmentLimitsDto } from '../../api';
import { AttachmentDto } from '../../core/models';

const TYPE_NAMES: Record<string, string> = {
  'image/png': 'PNG',
  'image/jpeg': 'JPEG',
  'image/webp': 'WebP',
  'image/gif': 'GIF',
};

/** "PNG, JPEG, WebP or GIF". */
function typeList(types: readonly string[]): string {
  const names = types.map((t) => TYPE_NAMES[t] ?? t.replace(/^image\//, '').toUpperCase());
  return names.length > 1 ? `${names.slice(0, -1).join(', ')} or ${names.at(-1)}` : (names[0] ?? '');
}

/** "10 MB", "1.5 MB", "500 KB". */
export function formatBytes(bytes: number): string {
  const mb = bytes / (1024 * 1024);
  if (mb >= 1) return `${Number.isInteger(mb) ? mb : mb.toFixed(1)} MB`;
  return `${Math.round(bytes / 1024)} KB`;
}

/**
 * Why a file cannot be attached, or null when it can, against the limits `/api/app` serves. The
 * server checks again; this only saves uploading something it would refuse.
 */
export function imageProblem(file: File, limits: AttachmentLimitsDto): string | null {
  if (!limits.contentTypes.includes(file.type))
    return `${file.name || 'That file'} is not a ${typeList(limits.contentTypes)} image.`;
  if (file.size > limits.maxBytes)
    return `${file.name || 'That image'} is over ${formatBytes(limits.maxBytes)}.`;
  return null;
}

const ATTACHMENT_PATH = /^\/api\/chats\/[0-9a-f-]{36}\/attachments\/[0-9a-f-]{36}$/i;

/**
 * Whether an image in a message body points at one of this app's own attachments. Only those may
 * render inside markdown: they come from the same server, so loading them leaks nothing.
 */
export function isAttachmentUrl(src: string): boolean {
  try {
    const own = new URL(environment.apiBaseUrl || window.location.origin, window.location.origin);
    const url = new URL(src, own);
    return url.origin === own.origin && ATTACHMENT_PATH.test(url.pathname) && !url.search;
  } catch {
    return false;
  }
}

/** An attachment's URL on this app's API, which the auth interceptor sends the token to. */
export function attachmentSrc(attachment: Pick<AttachmentDto, 'url'>): string {
  return `${environment.apiBaseUrl}${attachment.url}`;
}

/**
 * A message's images: one shows large at its own aspect ratio, several tile two across. Each opens
 * full size in a lightbox. They load through the API with the user's token, so they are never
 * plain `<img src>` links another member's browser could fetch without being in the chat.
 */
@Component({
  selector: 'app-message-images',
  imports: [PkAuthImage, HlmDialogImports],
  host: { class: 'block max-w-full' },
  template: `
    <div
      class="grid max-w-full gap-1"
      [class.grid-cols-2]="attachments().length > 1"
      [class.w-80]="attachments().length > 1"
    >
      @for (image of attachments(); track image.id; let i = $index) {
        <button
          type="button"
          class="bg-muted focus-visible:ring-ring/50 block max-w-full overflow-hidden rounded-xl outline-none focus-visible:ring-2"
          [class.aspect-square]="attachments().length > 1"
          [style.aspect-ratio]="attachments().length === 1 ? ratio(image) : null"
          [style.width]="attachments().length === 1 ? singleWidth(image) : null"
          [attr.aria-label]="'Open image ' + (i + 1) + ' from ' + senderName()"
          (click)="open.set(image.id)"
        >
          <pk-auth-image [url]="src(image)" [alt]="image.description ?? ''" class="size-full" />
        </button>
      }
    </div>

    <hlm-dialog [state]="open() ? 'open' : 'closed'" (closed)="open.set(null)">
      <hlm-dialog-content
        *hlmDialogPortal="let ctx"
        class="w-fit max-w-[calc(100vw-2rem)] p-2 sm:max-w-[min(90vw,72rem)]"
      >
        <h2 hlmDialogTitle class="sr-only">Image from {{ senderName() }}</h2>
        @if (openImage(); as image) {
          <pk-auth-image
            [url]="src(image)"
            [alt]="image.description ?? ''"
            class="min-h-40 min-w-40 rounded-lg"
            imgClass="max-h-[85vh] max-w-full object-contain"
          />
          @if (image.description) {
            <!-- w-0 min-w-full: the text wraps to the image's width instead of widening the box. -->
            <p
              hlmDialogDescription
              class="text-muted-foreground line-clamp-3 w-0 min-w-full px-1 pb-1 text-xs"
            >
              {{ image.description }}
            </p>
          }
        }
      </hlm-dialog-content>
    </hlm-dialog>
  `,
})
export class MessageImages {
  readonly attachments = input.required<AttachmentDto[]>();
  readonly senderName = input('');

  /** By id, so a description that lands while it is open shows up in the caption. */
  protected readonly open = signal<string | null>(null);
  protected readonly openImage = computed(
    () => this.attachments().find((a) => a.id === this.open()) ?? null,
  );

  protected src(image: AttachmentDto): string {
    return attachmentSrc(image);
  }

  /** Reserves the image's space before it loads, so the list does not jump. */
  protected ratio(image: AttachmentDto): string {
    return image.width && image.height ? `${image.width} / ${image.height}` : '4 / 3';
  }

  /** Wide images fill the bubble's width; tall ones are capped by height instead. */
  protected singleWidth(image: AttachmentDto): string {
    const maxHeight = 18; // rem
    const maxWidth = 20;
    if (!image.width || !image.height) return `${maxWidth}rem`;
    const width = Math.min(maxWidth, (maxHeight * image.width) / image.height);
    return `${Math.max(width, 6)}rem`;
  }
}
