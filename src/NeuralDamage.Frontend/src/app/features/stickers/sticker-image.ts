import { afterNextRender, Component, computed, DestroyRef, ElementRef, inject, input, signal } from '@angular/core';
import { AvatarImages } from '../../shared/avatar-images';
import { watchInView } from '../../shared/in-view';

/**
 * A sticker's picture, contained in a square of `px` pixels. It loads through the API with the
 * token, once per sticker however often it shows, and only once it comes near the screen, so a
 * library of thousands loads only what is looked at. Animated ones play.
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
      <img [src]="src" [alt]="alt()" loading="lazy" decoding="async" class="size-full object-contain" draggable="false" />
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
  /** Set once it has been near the screen; it then stays loaded. */
  private readonly seen = signal(false);
  protected readonly src = computed(() => (this.seen() ? this.images.get(this.url())() : null));

  constructor() {
    const element = inject(ElementRef<HTMLElement>).nativeElement as HTMLElement;
    let stop = () => {};
    afterNextRender(() => {
      stop = watchInView(element, (visible) => {
        if (!visible) return;
        this.seen.set(true);
        stop();
      });
    });
    inject(DestroyRef).onDestroy(() => stop());
  }
}
