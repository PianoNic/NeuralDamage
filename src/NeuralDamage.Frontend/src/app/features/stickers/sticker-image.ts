import { Component, computed, inject, input } from '@angular/core';
import { AvatarImages } from '../../shared/avatar-images';

/**
 * A sticker's picture, contained in a square of `px` pixels. It loads through the API with the
 * token, once per sticker however often it shows, and animated ones play.
 */
@Component({
  selector: 'app-sticker-image',
  host: {
    class: 'inline-flex shrink-0 items-center justify-center',
    '[style.width.px]': 'px()',
    '[style.height.px]': 'px()',
  },
  template: `
    @if (src(); as src) {
      <img [src]="src" [alt]="alt()" class="size-full object-contain" draggable="false" />
    } @else {
      <span class="bg-muted size-full animate-pulse rounded-xl" aria-hidden="true"></span>
    }
  `,
})
export class StickerImage {
  readonly url = input.required<string>();
  readonly alt = input('');
  readonly px = input(64);

  private readonly images = inject(AvatarImages);
  protected readonly src = computed(() => this.images.get(this.url())());
}
